module Mirage.Unity.AudioStream

open IcedTasks
open System
open System.Buffers
open System.Threading.Tasks
open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Logger
open Mirage.Domain.Audio.Packet
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Reader
open Mirage.Core.Audio.Opus.Codec

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
    {   /// 16-bit audio signal.
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

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable audioSender = None
    let mutable audioReceiver = None
    let event = Event<EventHandler<_>, _>()

    /// Run the callback if the sender client id matches the <b>AllowedSenderId</b> value.
    let onValidSender (serverRpcParams: ServerRpcParams) callback =
        let clientId = serverRpcParams.Receive.SenderClientId
        if self.IsHost && self.NetworkManager.ConnectedClients.ContainsKey clientId && Some clientId = self.AllowedSenderId then
            callback()
    
    /// Load the opus file, play it locally, while streaming the packets to all other clients to play.
    let streamAudioHost opusReader =
        iter dispose audioSender
        self.InitializeAudioReceiver opusReader.totalSamples
        self.InitializeAudioReceiverClientRpc opusReader.totalSamples
        let sendPacket packet =
            self.SendPacketClientRpc packet
            onReceivePacket audioReceiver packet
        audioSender <- Some <| AudioSender sendPacket opusReader self.destroyCancellationToken
        startAudioSender audioSender.Value
    
    /// Load the opus file, and then send the packets to the host. The host then relays it to all clients.
    let streamAudioClient opusReader =
        let serverRpcParams = ServerRpcParams()
        let sendPacket packet =
            self.SendPacketServerRpc(packet, serverRpcParams)
            ArrayPool.Shared.Return packet.opusData
        self.InitializeAudioReceiverServerRpc(opusReader.totalSamples, serverRpcParams)
        audioSender <- Some <| AudioSender sendPacket opusReader self.destroyCancellationToken
        startAudioSender audioSender.Value

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
    member this.StreamOpusFromFile filePath =
        valueTask {
            let localId = StartOfRound.Instance.localPlayerController.actualClientId
            if Some localId <> this.AllowedSenderId then
                invalidOp $"StreamAudioFromFile cannot be run from this client. LocalId: {localId}. AllowedId: {this.AllowedSenderId}."
            else
                let! opusReader = readOpusFile filePath
                // opusReader could be disposed by the time Async.Sleep is called.
                // This is cached to avoid failing to grab the amount of milliseconds to wait.
                let totalTime = int opusReader.reader.CurrentTime.TotalMilliseconds
                try
                    if this.IsHost then streamAudioHost opusReader
                    else streamAudioClient opusReader
                with | error ->
                    logError $"An exception occured while streaming audio: {error}"
                do! Task.Delay(totalTime, this.destroyCancellationToken)
        }

    /// Initialize the audio receiver to playback audio when opus packets are received.
    member this.InitializeAudioReceiver totalSamples =
        iter dispose audioReceiver
        let triggerEvent (decodedPacket: DecodedPacket) =
            let eventData =
                AudioReceivedEvent
                    {   samples = decodedPacket.samples
                        sampleIndex = decodedPacket.sampleIndex
                    }
            event.Trigger(self, eventData)
        audioReceiver <- Some <| AudioReceiver this.AudioSource totalSamples triggerEvent this.destroyCancellationToken
        startAudioReceiver audioReceiver.Value
        let eventData =
            AudioStartEvent
                {   lengthSamples = totalSamples
                    channels = OpusChannels
                    frequency = OpusSampleRate
                }
        event.Trigger(this, eventData)
    
    [<ClientRpc>]
    member this.InitializeAudioReceiverClientRpc totalSamples =
        if not this.IsHost then
            this.InitializeAudioReceiver totalSamples

    [<ServerRpc(RequireOwnership = false)>]
    member this.InitializeAudioReceiverServerRpc(totalSamples, serverRpcParams) =
        onValidSender serverRpcParams <| fun () ->
            this.InitializeAudioReceiver totalSamples
            this.InitializeAudioReceiverClientRpc totalSamples 

    /// Send the current frame data to each client.
    [<ClientRpc>]
    member this.SendPacketClientRpc opusPacket =
        if not this.IsHost then
            onReceivePacket audioReceiver opusPacket
    
    [<ServerRpc(RequireOwnership = false)>]
    member this.SendPacketServerRpc(opusPacket, serverRpcParams) =
        onValidSender serverRpcParams <| fun () ->
            onReceivePacket audioReceiver opusPacket
            this.SendPacketClientRpc opusPacket