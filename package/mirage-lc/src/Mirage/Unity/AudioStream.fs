module Mirage.Unity.AudioStream

open System
open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Reader
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Logger

[<Struct>]
type AudioStartEvent =
    {   /// Number of sample frames.
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
    let triggerEvent (decodedPacket: DecodedPacket) =
        let eventData =
            AudioReceivedEvent
                {   samples = decodedPacket.samples
                    sampleIndex = decodedPacket.sampleIndex
                }
        event.Trigger(self, AudioStreamEventArgs(eventData))

    /// Run the callback if the sender client id matches the <b>AllowedSenderId</b> value.
    let onValidSender (this: NetworkBehaviour) (serverRpcParams: ServerRpcParams) callback =
        let clientId = serverRpcParams.Receive.SenderClientId
        if this.IsHost && this.NetworkManager.ConnectedClients.ContainsKey clientId && Some clientId = self.AllowedSenderId then
            callback()
    
    /// Load the opus file, play it locally, while streaming the packets to all other clients to play.
    let streamAudioHost opusReader =
        async {
            try
                let pcmHeader = PcmHeader opusReader
                let sendPacket (opusPacket: OpusPacket) =
                    onReceivePacket opusPacket audioReceiver
                iter dispose audioSender
                self.InitializeAudioReceiver pcmHeader
                self.InitializeAudioReceiverClientRpc pcmHeader
                audioSender <- Some <| AudioSender sendPacket opusReader self.destroyCancellationToken
                startAudioSender audioSender.Value
            with | error -> logError $"An exception occured while running streamAudioHost: {error}"
            do! Async.Sleep(int opusReader.reader.TotalTime.TotalMilliseconds)
        }
    
    /// Load the opus file, and then send the packets to the host. The host then relays it to all clients.
    let streamAudioClient opusReader =
        async {
            try
                let pcmHeader = PcmHeader opusReader
                let serverRpcParams = ServerRpcParams()
                let sendPacket opusPacket = self.SendPacketServerRpc(opusPacket, serverRpcParams)
                self.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams)
                audioSender <- Some <| AudioSender sendPacket opusReader self.destroyCancellationToken
                startAudioSender audioSender.Value
            with | error -> logError $"An exception occured while running streamAudioClient: {error}"
            do! Async.Sleep(int opusReader.reader.TotalTime.TotalMilliseconds)
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
                let! opusReader = readOpusFile filePath
                if this.IsHost then
                    do! streamAudioHost opusReader
                else
                    do! streamAudioClient opusReader
        }

    /// Initialize the audio receiver to playback audio when opus packets are received.
    member this.InitializeAudioReceiver(pcmHeader) =
        iter dispose audioReceiver
        audioReceiver <- Some <| AudioReceiver this.AudioSource pcmHeader triggerEvent this.destroyCancellationToken
        let eventData =
            AudioStartEvent
                {   lengthSamples = pcmHeader.totalSamples
                    channels = pcmHeader.channels
                    frequency = pcmHeader.sampleRate
                }
        event.Trigger(this, AudioStreamEventArgs(eventData))
    
    [<ClientRpc>]
    member this.InitializeAudioReceiverClientRpc(pcmHeader) =
        if not this.IsHost then
            this.InitializeAudioReceiver pcmHeader

    [<ServerRpc(RequireOwnership = false)>]
    member this.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            this.InitializeAudioReceiver pcmHeader
            this.InitializeAudioReceiverClientRpc pcmHeader

    /// Send the current frame data to each client.
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member this.SendPacketClientRpc(opusPacket) =
        if not this.IsHost then
            onReceivePacket opusPacket audioReceiver
    
    [<ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)>]
    member this.SendPacketServerRpc(opusPacket, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            onReceivePacket opusPacket audioReceiver
            this.SendPacketClientRpc opusPacket