module Mirage.Unity.AudioStream

open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Core.Audio.File.Mp3Reader
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Audio.Frame
open Mirage.Domain.Logger

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable audioReceiver: Option<AudioReceiver> = None

    /// Run the callback if the sender client id matches the <b>AllowedSenderId</b> value.
    let onValidSender (this: NetworkBehaviour) (serverRpcParams: ServerRpcParams) callback =
        let clientId = serverRpcParams.Receive.SenderClientId
        if this.IsHost && this.NetworkManager.ConnectedClients.ContainsKey clientId && Some clientId = self.AllowedSenderId then
            callback()

    /// Load the mp3 file and play it locally, while sending the audio to play on all other clients.
    let streamAudioHost mp3Reader =
        async {
            let pcmHeader = PcmHeader mp3Reader
            iter dispose audioReceiver
            audioReceiver <- Some <| AudioReceiver self.AudioSource pcmHeader
            let onFrameRead frameData =
                onReceiveFrame audioReceiver.Value frameData
                self.SendFrameClientRpc frameData
            self.InitializeAudioReceiverClientRpc pcmHeader
            use audioSender = AudioSender onFrameRead mp3Reader
            sendAudio audioSender
            do! Async.Sleep(int mp3Reader.reader.TotalTime.TotalMilliseconds)
        }

    /// Load the mp3 file, and then send it to the server to broadcast to all other clients.
    let streamAudioClient mp3Reader =
        async {
            logInfo "streamAudioClient running"
            let pcmHeader = PcmHeader mp3Reader
            let serverRpcParams = ServerRpcParams()
            let sendFrame frameData = self.SendFrameServerRpc(frameData, serverRpcParams)
            self.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams)
            use audioSender = AudioSender sendFrame mp3Reader
            sendAudio audioSender
            logInfo $"total seconds: {mp3Reader.reader.TotalTime.TotalSeconds}"
            logInfo $"total ms as seconds: {int <| mp3Reader.reader.TotalTime.TotalMilliseconds / 1000.0}"
            do! Async.Sleep(int mp3Reader.reader.TotalTime.TotalMilliseconds)
        }

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
                let! mp3Reader = readMp3File filePath
                if this.IsHost then
                    do! streamAudioHost mp3Reader
                else 
                    do! streamAudioClient mp3Reader
        }

    /// Initialize the audio receiver to playback audio when audio frames are received.
    member this.InitializeAudioReceiver(pcmHeader) =
        iter dispose audioReceiver
        audioReceiver <- Some <| AudioReceiver this.AudioSource pcmHeader

    [<ServerRpc(RequireOwnership = false)>]
    member this.InitializeAudioReceiverServerRpc(pcmHeader, serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            this.InitializeAudioReceiver pcmHeader
            this.InitializeAudioReceiverClientRpc pcmHeader

    [<ClientRpc>]
    member this.InitializeAudioReceiverClientRpc(pcmHeader) =
        if not this.IsHost then
            this.InitializeAudioReceiver pcmHeader

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