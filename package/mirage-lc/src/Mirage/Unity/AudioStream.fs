module Mirage.Unity.AudioStream

open System.IO
open FSharpPlus
open UnityEngine
open Unity.Netcode
open NAudio.Wave
open Mirage.Core.Audio.File.Mp3Reader
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Audio.Frame
open Mirage.Domain.Logger

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable currentUpload: MemoryStream = null
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
            self.InitializeAudioClientRpc pcmHeader
            use audioSender = AudioSender onFrameRead (konst ()) mp3Reader
            sendAudio audioSender
            do! Async.Sleep(int mp3Reader.reader.TotalTime.TotalMilliseconds)
        }

    /// Load the mp3 file, and then send it to the server to broadcast to all other clients.
    let streamAudioClient mp3Reader =
        async {
            logInfo "streamAudioClient running"
            let serverRpcParams = ServerRpcParams()
            let sendFrame frameData = self.SendFrameServerRpc(frameData, serverRpcParams)
            self.InitializeAudioServerRpc serverRpcParams
            sendFrame <| mp3Reader.reader.XingHeader.frame.RawData
            let onFinish () = self.FinishUploadServerRpc serverRpcParams
            use audioSender = AudioSender (sendFrame << _.rawData) onFinish mp3Reader
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
        if not <| isNull currentUpload then
            dispose currentUpload

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

    /// Initialize host by creating a new memory stream to write to.
    [<ServerRpc(RequireOwnership = false)>]
    member this.InitializeAudioServerRpc(serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            if not <| isNull currentUpload then
                dispose currentUpload
            currentUpload <- new MemoryStream()

    /// Initialize clients by setting the pcm header on each client.
    [<ClientRpc>]
    member this.InitializeAudioClientRpc(pcmHeader) =
        if not this.IsHost then
            iter dispose audioReceiver
            audioReceiver <- Some <| AudioReceiver this.AudioSource pcmHeader

    /// Send the current frame data to the host, to eventually broadcast to all other clients.
    [<ServerRpc(RequireOwnership = false)>]
    member this.SendFrameServerRpc(rawData: byte[], serverRpcParams) =
        onValidSender this serverRpcParams <| fun () ->
            currentUpload.Write rawData

    /// Send the current frame data to each client.
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member this.SendFrameClientRpc(frameData) =
        if not this.IsHost then
            onReceiveFrame audioReceiver.Value frameData
            if not this.AudioSource.isPlaying then this.AudioSource.Play()

    /// Start broadcasting the uploaded audio to all other clients.
    [<ServerRpc(RequireOwnership = false)>]
    member this.FinishUploadServerRpc(serverRpcParams) =
        logInfo "finished upload server rpc (outside)"
        onValidSender this serverRpcParams <| fun () ->
            logInfo "finished upload server rpc (inside)"
            currentUpload.Position <- 0
            let mp3Stream = new MemoryStream(currentUpload.ToArray())
            dispose currentUpload
            currentUpload <- null
            let mp3Reader = { reader = new Mp3FileReader(mp3Stream) }
            Async.StartImmediate <| streamAudioHost mp3Reader