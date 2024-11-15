module Mirage.Unity.AudioStream

open System
open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.File.Mp3Reader
open Mirage.Domain.Logger
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Audio.Frame

type AudioStartEvent =
    {   /// Number of sample frames.
        lengthSamples: int
        /// Number of channels per frame.
        channels: int
        /// Sample frequency of the audio.
        frequency: int
    }

type AudioReceivedEvent =
    {   /// Audio signal containing a single decompressed mp3 frame.
        samples: Samples
        /// Index of where the sample belongs in, relative to the whole audio clip.
        sampleIndex: int
    }

type AudioStreamEvent
    /// Event that is trigered when a new audio clip begins.
    = AudioStartEvent of AudioStartEvent
    /// Event that is trigered when audio samples are received.
    | AudioReceivedEvent of AudioReceivedEvent

type AudioStreamEventArgs(eventData: AudioStreamEvent) =
    inherit EventArgs()
    member _.EventData = eventData

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable audioSender: Option<AudioSender> = None
    let mutable audioReceiver: Option<AudioReceiver> = None

    let event = Event<EventHandler<_>, _>()
    let onFrameDecompressed (samples: Samples) sampleIndex =
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

    /// Load the mp3 file and play it locally, while sending the audio to play on all other clients.
    let streamAudioHost mp3Reader guid =
        async {
            try
                //logError $"streamAudioHost ({guid}): start"
                iter dispose audioSender
                let pcmHeader = PcmHeader mp3Reader

                //iter dispose audioReceiver
                //logError $"streamAudioHost ({guid}): AudioReceiver"
                //audioReceiver <- Some <| AudioReceiver self.AudioSource pcmHeader onFrameDecompressed

                let onFrameRead frameData =
                    // non-null check for self instance is necessary because this function is still called
                    // when returning to the main menu.
                    if not (isNull self) && Option.isSome audioReceiver then
                        onReceiveFrame audioReceiver.Value frameData
                        self.SendFrameClientRpc frameData
                //logError $"streamAudioHost ({guid}): InitializeAudioReceiverClientRpc"
                self.InitializeAudioReceiver pcmHeader
                self.InitializeAudioReceiverClientRpc pcmHeader
                //logError $"streamAudioHost ({guid}): AudioSender"
                audioSender <- Some <| AudioSender onFrameRead mp3Reader
                //logError $"streamAudioHost ({guid}): Before sendAudio"
                sendAudio audioSender.Value
                //logError $"streamAudioHost ({guid}): After sendAudio"
            with | error -> logError $"Exception found while running streamAudioHost: {error}"
            //let seconds = (mp3Reader.reader.TotalTime.TotalMilliseconds / 1000.0).ToString("F2")
            //logInfo $"streamAudioHost ({guid}): sleeping for {seconds} seconds"
            do! Async.Sleep(int mp3Reader.reader.TotalTime.TotalMilliseconds)
            //logInfo $"streamAudioHost ({guid}): finished sleeping for {seconds} seconds"
        }

    /// Load the mp3 file, and then send it to the server to broadcast to all other clients.
    let streamAudioClient mp3Reader =
        async {
            try
                iter dispose audioSender
                let pcmHeader = PcmHeader mp3Reader
                let serverRpcParams = ServerRpcParams()
                let sendFrame frameData = self.SendFrameServerRpc(frameData, serverRpcParams)
                self.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams)
                audioSender <- Some <| AudioSender sendFrame mp3Reader
                sendAudio audioSender.Value
            with | error -> logError $"Exception found while running streamAudioClient: {error}"
            //let seconds = (waveReader.mp3Reader.TotalTime.TotalMilliseconds / 1000.0).ToString("F2")
            //logInfo $"streamAudioClient. sleeping for {seconds} seconds"
            do! Async.Sleep(int mp3Reader.reader.TotalTime.TotalMilliseconds)
        }

    /// An event that triggers when a new audio clip begins.
    [<CLIEvent>]
    member _.OnAudioStream =  event.Publish

    member val AudioSource: AudioSource = null with get, set

    /// The client id of the client that is allowed to broadcast audio to other clients.
    member val AllowedSenderId: Option<uint64> = None with get, set

    override _.OnDestroy() =
        base.OnDestroy()
        iter dispose audioReceiver

    /// Stream audio from the player (can be host or non-host) to all other players.
    member this.StreamAudioFromFile(filePath, guid) =
        async {
            let localId = StartOfRound.Instance.localPlayerController.actualClientId
            if Some localId <> this.AllowedSenderId then
                invalidOp $"StreamAudioFromFile cannot be run from this client. LocalId: {localId}. AllowedId: {this.AllowedSenderId}."
            else
                let! mp3Reader = readMp3File filePath
                if this.IsHost then
                    do! streamAudioHost mp3Reader guid
                else 
                    do! streamAudioClient mp3Reader
        }

    /// Initialize the audio receiver to playback audio when audio frames are received.
    member this.InitializeAudioReceiver(pcmHeader) =
        iter dispose audioReceiver
        audioReceiver <- Some <| AudioReceiver this.AudioSource pcmHeader onFrameDecompressed

    [<ServerRpc(RequireOwnership = false)>]
    member this.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            this.InitializeAudioReceiver pcmHeader
            this.InitializeAudioReceiverClientRpc pcmHeader

    [<ClientRpc>]
    member this.InitializeAudioReceiverClientRpc(pcmHeader) =
        if not this.IsHost then
            this.InitializeAudioReceiver pcmHeader
        let eventData =
            AudioStartEvent
                {   lengthSamples = pcmHeader.samples
                    channels = pcmHeader.channels
                    frequency = pcmHeader.frequency
                }
        event.Trigger(this, AudioStreamEventArgs(eventData))

    /// Send the current frame data to the host, to eventually broadcast to all other clients.
    [<ServerRpc(RequireOwnership = false)>]
    member this.SendFrameServerRpc(frameData, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            if audioReceiver.IsSome then
                onReceiveFrame audioReceiver.Value frameData
            this.SendFrameClientRpc frameData

    /// Send the current frame data to each client.
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member this.SendFrameClientRpc(frameData) =
        if not this.IsHost && audioReceiver.IsSome then
            onReceiveFrame audioReceiver.Value frameData