module Mirage.Unity.AudioStream

open System
open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Logger
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Wave.Reader
open Mirage.Domain.Audio.Packet

[<Struct>]
type AudioStartEvent =
    {   /// Number of samples in the audio clip.
        lengthSamples: int
        /// Number of channels per frame.
        channels: int
        /// Sample frequency of the audio.
        frequency: int
    }


[<Struct>]
type AudioReceivedEvent =
    {   /// Audio signal containing a single decompressed mp3 frame.
        samples: Samples
        /// Index of where the sample belongs in, relative to the whole audio clip.
        sampleIndex: int
    }

[<Struct>]
type AudioStreamEvent
    /// Event that is trigered when a new audio clip begins.
    = AudioStartEvent of audioStartEvent: AudioStartEvent
    /// Event that is trigered when audio samples are received.
    | AudioReceivedEvent of audioReceivedEvent: AudioReceivedEvent

type AudioStreamEventArgs(eventData: AudioStreamEvent) =
    inherit EventArgs()
    member _.EventData = eventData

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable audioSender: Option<AudioSender> = None
    let mutable audioReceiver: Option<AudioReceiver> = None

    let event = Event<EventHandler<_>, _>()
    let triggerEvent samples sampleIndex =
        let eventData =
            AudioReceivedEvent
                {   samples = samples
                    sampleIndex = sampleIndex
                }
        event.Trigger(self, AudioStreamEventArgs(eventData))

    /// Run the callback if the sender client id matches the <b>AllowedSenderId</b> value.
    let onValidSender (this: NetworkBehaviour) (serverRpcParams: ServerRpcParams) callback =
        let clientId = serverRpcParams.Receive.SenderClientId
        if this.IsHost && this.NetworkManager.ConnectedClients.ContainsKey clientId && Some clientId = self.AllowedSenderId then
            callback()
    
    /// Load the opus file, play it locally, while streaming the packets to all other clients to play.
    let streamAudioHost waveReader =
        async {
            let waveHeader = WaveHeader waveReader
            iter dispose audioSender
            self.InitializeAudioReceiver waveHeader
            self.InitializeAudioReceiverClientRpc waveHeader
            audioSender <- Some <| AudioSender (flip onReceivePacket audioReceiver) waveReader self.destroyCancellationToken
            startAudioSender audioSender.Value
        }
    
    /// Load the opus file, and then send the packets to the host. The host then relays it to all clients.
    let streamAudioClient waveReader =
        async {
            let waveHeader = WaveHeader waveReader
            let serverRpcParams = ServerRpcParams()
            let sendPacket opusPacket = self.SendPacketServerRpc(opusPacket, serverRpcParams)
            self.InitializeAudioReceiverServerRpc(waveHeader, serverRpcParams)
            audioSender <- Some <| AudioSender sendPacket waveReader self.destroyCancellationToken
            startAudioSender audioSender.Value
        }

    /// An event that triggers when a new audio clip begins.
    [<CLIEvent>]
    member _.OnAudioStream =  event.Publish

    member val AudioSource: AudioSource = null with get, set

    /// The client id of the client that is allowed to broadcast audio to other clients.
    member val AllowedSenderId: Option<uint64> = None with get, set

    override _.OnDestroy() =
        base.OnDestroy()
        iter dispose audioSender
        iter dispose audioReceiver

    /// Stream audio from the player (can be host or non-host) to all other players.
    member this.StreamOpusFromFile(filePath) =
        async {
            let localId = StartOfRound.Instance.localPlayerController.actualClientId
            if Some localId <> this.AllowedSenderId then
                invalidOp $"StreamAudioFromFile cannot be run from this client. LocalId: {localId}. AllowedId: {this.AllowedSenderId}."
            else
                let! waveReader = readWavFile filePath
                // waveReader could be disposed by the time Async.Sleep is called.
                // This is cached to avoid failing to grab the amount of milliseconds to wait.
                let totalTime = int waveReader.reader.TotalTime.TotalMilliseconds
                try
                    if this.IsHost then
                        do! streamAudioHost waveReader
                    else
                        do! streamAudioClient waveReader
                with | error ->
                    logError $"An exception occured while streaming audio: {error}"
                do! Async.Sleep totalTime
        }

    /// Initialize the audio receiver to playback audio when opus packets are received.
    member this.InitializeAudioReceiver(waveHeader) =
        iter dispose audioReceiver
        audioReceiver <- Some <| AudioReceiver this.AudioSource waveHeader triggerEvent this.destroyCancellationToken
        startAudioReceiver audioReceiver.Value
        let eventData =
            AudioStartEvent
                {   lengthSamples = waveHeader.lengthSamples
                    channels = waveHeader.channels
                    frequency = waveHeader.frequency
                }
        event.Trigger(this, AudioStreamEventArgs(eventData))
    
    [<ClientRpc>]
    member this.InitializeAudioReceiverClientRpc(waveHeader) =
        if not this.IsHost then
            this.InitializeAudioReceiver waveHeader

    [<ServerRpc(RequireOwnership = false)>]
    member this.InitializeAudioReceiverServerRpc(waveHeader, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            this.InitializeAudioReceiver waveHeader
            this.InitializeAudioReceiverClientRpc waveHeader

    /// Send the current frame data to each client.
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member this.SendPacketClientRpc(packet) =
        if not this.IsHost then
            onReceivePacket packet audioReceiver
    
    [<ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)>]
    member this.SendPacketServerRpc(packet, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            onReceivePacket packet audioReceiver
            this.SendPacketClientRpc packet