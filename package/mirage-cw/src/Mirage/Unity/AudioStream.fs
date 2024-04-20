module Mirage.Unity.AudioStream

open FSharpPlus
open NAudio.Wave
open UnityEngine
open System.IO
open System.Threading
open Microsoft.FSharp.Data.UnitSystems.SI.UnitNames
open MyceliumNetworking
open Mirage.Core.Logger
open Mirage.Core.Monad
open Mirage.Core.Field
open Mirage.Core.Audio.Format
open Mirage.Core.Audio.Data
open Mirage.Core.Audio.Network.Sender
open Mirage.Core.Audio.Network.Receiver
open Mirage.Unity.RpcBehaviour

let [<Literal>] ReceiverTimeout = 30<second>
let private get<'A> : Getter<'A> = getter "AudioStream"

[<AllowNullLiteral>]
type AudioStream() =
    inherit RpcBehaviour()

    let AudioSource = field<AudioSource>()

    /// <b>Host only.</b><br />
    /// Send audio to receivers from the host.
    let AudioSender = field<AudioSender>()

    /// <b>Non-host only.</b><br />
    /// Receive audio on non-hosts.
    let AudioReceiver = field<AudioReceiver>()
    
    /// <b>Non-host only.</b><br />
    /// Send audio to the host to broadcast to enemies.
    let AudioUploader = field<AudioSender>()
    
    /// <b>Host only.</b><br />
    /// The client id to accept audio bytes from, and
    /// the current upload bytes of compressed audio.
    let CurrentUpload = field<Player * MemoryStream>()

    let getAudioSource = get AudioSource "AudioSource"
    let getAudioSender = get AudioSender "AudioSender"
    let getAudioReceiver = get AudioReceiver "AudioReceiver"
    let getCurrentUpload = get CurrentUpload "CurrentUpload"

    let canceller = new CancellationTokenSource()

    let stopAudioSender() =
        iter stopSender AudioSender.Value
        setNone AudioSender

    let stopAudioReciever() =
        iter stopReceiver AudioReceiver.Value
        setNone AudioReceiver

    let stopAll () =
        flip iter AudioSource.Value <| fun audioSource ->
            UnityEngine.Object.Destroy audioSource.clip
        stopAudioSender()
        stopAudioReciever()
        iter (dispose << snd) CurrentUpload.Value

    /// Stream audio from the host to all clients.
    let streamAudioFromHost (this: AudioStream) (audioReader: Mp3FileReader)  =
        handleResultWith stopAll <| monad' {
            stopAudioSender()
            let sendFrameClientRpc (frameData: FrameData) =
                clientRpc' this ReliableType.UnreliableNoDelay "SendFrameClientRpc"
                    [|  frameData.rawData
                        frameData.sampleIndex
                    |]
            let finishAudioClientRpc () = clientRpc this "FinishAudioClientRpc" zero
            let (audioSender, pcmHeader) = startSender sendFrameClientRpc finishAudioClientRpc audioReader
            set AudioSender audioSender

            // A better approach is to have the client notify the server when it's ready,
            // but this requires keeping track of the audio state separately for each client.

            // Since I want to only have one audio reader, we wait for a bit and assume the client
            // has initialized its audio clip.

            // If the client is late and hasn't been initialized by the time it starts to receive audio frames,
            // it will play silent noise until it reaches the earliest frame it receives.
            let! audioSender = getAudioSender "streamAudioFromHost"
            runAsync canceller.Token <| async {
                clientRpc this "InitializeAudioClientRpc"
                    [|  pcmHeader.samples
                        pcmHeader.channels
                        pcmHeader.frequency
                        pcmHeader.blockSize
                        pcmHeader.bitRate
                    |]
                do! Async.Sleep 1000 // Wait a second for the clients to initialize its audio clip.
                sendAudio audioSender
            }
        }

     /// Play the given audio file locally.
    let playAudio (filePath: string) =
        async {
            let! audioClip =
                forkReturn <|
                    async {
                        use audioReader = new WaveFileReader(filePath)
                        return convertToAudioClip audioReader
                    }
            handleResult <| monad' {
                let! audioSource = getAudioSource "playAudio"
                audioSource.Stop()
                UnityEngine.Object.Destroy audioSource.clip
                audioSource.clip <- audioClip
                audioSource.Play()
            }
        }

    member this.StreamAudioFromFile(filePath) =
        if this.IsHost then
            runAsync canceller.Token <| async {
                let! audioReader =
                    forkReturn <| async {
                        use audio = new AudioFileReader(filePath)
                        return compressAudio audio
                    }
                streamAudioFromHost this audioReader
                return! playAudio filePath
            }

    /// Initialize the client by sending it the required pcm header.<br />
    /// This is followed by the starting the stream on the server.
    [<CustomRPC>]
    member this.InitializeAudioClientRpc(samples, channels, frequency, blockSize, bitRate) =
        handleResultWith stopAll <| monad' {
            if not this.IsHost then
                stopAudioReciever()
                let! audioSource = getAudioSource "InitializeAudioClientRpc"
                let receiver =
                    startReceiver audioSource
                        {   samples = samples
                            channels = channels
                            frequency = frequency
                            blockSize = blockSize
                            bitRate = bitRate
                        }
                set AudioReceiver receiver
                runAsync this.destroyCancellationToken <| startTimeout receiver ReceiverTimeout
        }

     /// Send audio frame data to the client to play.
    [<CustomRPC>]
    member this.SendFrameClientRpc(rawData, sampleIndex) =
        handleResult <| monad' {
            if not this.IsHost then
                let! audioReceiver = getAudioReceiver "SendFrameClientRpc"
                setFrameData audioReceiver { rawData = rawData; sampleIndex = sampleIndex }
        }

    [<CustomRPC>]
    member this.FinishAudioClientRpc() =
        if not this.IsHost then
            iter stopTimeout AudioReceiver.Value

    /// Stream the given audio file to the host and all clients.<br />
    /// Audio is only streamed if the client id matches the sender.
    /// Note: This can only be invoked by a non-host.
    member this.UploadAndStreamAudioFromFile(clientId: int, filePath: string) =
        if this.IsHost then
            logError "This can only be invoked by non-hosts."
        else
            iter stopSender AudioUploader.Value
            setNone AudioUploader
            runAsync canceller.Token <| async {
                let! audioReader =
                    forkReturn <|
                        async {
                            use audioReader = new AudioFileReader(filePath)
                            return compressAudio audioReader
                        }
                serverRpc this "InitializeUploadServerRpc" [|clientId|]
                let sendFrame rawData =
                    serverRpc this "UploadFrameServerRpc" [|rawData; clientId|]
                sendFrame audioReader.xingHeader.frame.RawData
                let onFinish () =
                    serverRpc this "UploadAudioFinishedServerRpc" [|clientId|]
                    setNone AudioUploader
                let (sender, _) = startSender (sendFrame << _.rawData) onFinish audioReader
                sendAudio sender
                set AudioUploader sender
            }

    /// Initialize the audio upload by sending the server the required pcm header.
    [<CustomRPC>]
    member this.InitializeUploadServerRpc(clientId) =
        handleResult <| monad' {
            if this.IsHost then
                iter (dispose << snd) CurrentUpload.Value
                let! uploader =
                    PlayerHandler.instance.players
                        |> tryFind (fun player -> player.refs.view.ViewID = clientId) 
                        |> Option.toResultWith $"Uploading player viewId was not found: {clientId}"
                set CurrentUpload (uploader, new MemoryStream())
        }

    /// Initialize the audio upload by sending the server the required pcm header.
    [<CustomRPC>]
    member this.UploadFrameServerRpc(frameData: byte[], clientId) =
        ignore <| monad' {
            if this.IsHost then
                let! (player, uploadStream) = getCurrentUpload "UploadFrameServerRpc"
                if clientId = player.refs.view.ViewID then
                    uploadStream.Write frameData
        }

    /// Broadcast the uploaded audio.
    [<CustomRPC>]
    member this.UploadAudioFinishedServerRpc(clientId) =
        handleResult <| monad' {
            if this.IsHost then
                let! (player, uploadStream) = getCurrentUpload "StreamAudioFromUploadServerRpc"
                if clientId = player.refs.view.ViewID then
                    uploadStream.Position <- 0
                    let audioData = uploadStream.ToArray()
                    uploadStream.Dispose()
                    setNone CurrentUpload

                    // Create a new stream for to play directly on the host.
                    use playbackStream = new MemoryStream(audioData)
                    use playbackMp3 = new Mp3FileReader(playbackStream)
                    let playbackWav = decompressAudio playbackMp3
                    let audioClip = convertToAudioClip playbackWav
                    flip iter AudioSource.Value <| fun audioSource ->
                        audioSource.Stop()
                        UnityEngine.Object.Destroy audioSource.clip
                        audioSource.clip <- audioClip
                        audioSource.Play()

                    streamAudioFromHost this <| new Mp3FileReader(new MemoryStream(audioData))
            }

    member _.SetAudioSource(audioSource) = set AudioSource audioSource
    member _.GetAudioSource() = getAudioSource "GetAudioSource"

    member _.SetAudioMixer(player: Player) =
        handleResult <| monad' {
            let! audioSource = getAudioSource "SetAudioMixer"
            audioSource.outputAudioMixerGroup <- player.transform.Find("HeadPosition/Voice").GetComponent<AudioSource>().outputAudioMixerGroup
        }