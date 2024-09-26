module Mirage.Unity.AudioStream

open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.File.WaveReader
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

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable audioReceiver: Option<AudioReceiver> = None

    let event = Event<AudioStreamEvent>()
    let onFrameDecompressed (samples: Samples) sampleIndex =
        event.Trigger <|
            AudioReceivedEvent
                {   samples = samples
                    sampleIndex = sampleIndex
                }

    /// Run the callback if the sender client id matches the <b>AllowedSenderId</b> value.
    let onValidSender (this: NetworkBehaviour) (serverRpcParams: ServerRpcParams) callback =
        let clientId = serverRpcParams.Receive.SenderClientId
        if this.IsHost && this.NetworkManager.ConnectedClients.ContainsKey clientId && Some clientId = self.AllowedSenderId then
            callback()

    /// Load the mp3 file and play it locally, while sending the audio to play on all other clients.
    let streamAudioHost waveReader =
        async {
            let pcmHeader = PcmHeader waveReader
            iter dispose audioReceiver
            audioReceiver <- Some <| AudioReceiver self.AudioSource pcmHeader onFrameDecompressed
            let onFrameRead frameData =
                onReceiveFrame audioReceiver.Value frameData
                self.SendFrameClientRpc frameData
            self.InitializeAudioReceiverClientRpc pcmHeader
            use audioSender = AudioSender onFrameRead waveReader
            sendAudio audioSender
            do! Async.Sleep(int waveReader.mp3Reader.TotalTime.TotalMilliseconds)
        }

    /// Load the mp3 file, and then send it to the server to broadcast to all other clients.
    let streamAudioClient waveReader =
        async {
            let pcmHeader = PcmHeader waveReader
            let serverRpcParams = ServerRpcParams()
            let sendFrame frameData = self.SendFrameServerRpc(frameData, serverRpcParams)
            self.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams)
            use audioSender = AudioSender sendFrame waveReader
            sendAudio audioSender
            do! Async.Sleep(int waveReader.mp3Reader.TotalTime.TotalMilliseconds)
        }

    /// An event that triggers when a new audio clip begins.
    member val OnAudioStream =  event.Publish

    member val AudioSource: AudioSource = null with get, set

    /// The client id of the client that is allowed to broadcast audio to other clients.
    member val AllowedSenderId: Option<uint64> = None with get, set

    override _.OnDestroy() =
        base.OnDestroy()
        iter dispose audioReceiver

    /// Stream audio from the player (can be host or non-host) to all other players.
    member this.StreamAudioFromFile(filePath) =
        async {
            let localId = StartOfRound.Instance.localPlayerController.actualClientId
            if Some localId <> this.AllowedSenderId then
                invalidOp $"StreamAudioFromFile cannot be run from this client. LocalId: {localId}. AllowedId: {this.AllowedSenderId}."
            else
                let! waveReader = readWavFile filePath
                if this.IsHost then
                    do! streamAudioHost waveReader
                else 
                    do! streamAudioClient waveReader
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
        event.Trigger <|
            AudioStartEvent
                {   lengthSamples = pcmHeader.samples
                    channels = pcmHeader.channels
                    frequency = pcmHeader.frequency
                }

    /// Send the current frame data to the host, to eventually broadcast to all other clients.
    [<ServerRpc(RequireOwnership = false)>]
    member this.SendFrameServerRpc(frameData, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            onReceiveFrame audioReceiver.Value frameData
            this.SendFrameClientRpc frameData

    /// Send the current frame data to each client.
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member this.SendFrameClientRpc(frameData) =
        if not this.IsHost then
            onReceiveFrame audioReceiver.Value frameData
            if not this.AudioSource.isPlaying then this.AudioSource.Play()