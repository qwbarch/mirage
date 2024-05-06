module Mirage.Unity.AudioStream

open System
open FSharpPlus
open FSharpx.Control
open UnityEngine
open Unity.Netcode
open NAudio.Wave
open Mirage.Core.Audio.File.Mp3Reader
open Mirage.Domain.Audio.Sender
open Mirage.Domain.Audio.Receiver
open Mirage.Domain.Audio.Frame
open Mirage.Domain.Logger
open Mirage.Core.Async.Fork

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let mutable audioReceiver: Option<AudioReceiver> = None

    /// Load the mp3 file and play it locally, while sending the audio to play on all other clients.
    let streamAudioHost filePath =
        async {
            let! mp3Reader = readMp3File filePath
            let pcmHeader = PcmHeader mp3Reader
            iter dispose audioReceiver
            audioReceiver <- Some <| AudioReceiver self.AudioSource pcmHeader
            let onFrameRead frameData =
                onReceiveFrame audioReceiver.Value frameData
                self.SendFrameClientRpc frameData
            self.InitializeAudioClientRpc pcmHeader
            use audioSender = AudioSender onFrameRead mp3Reader
            sendAudio audioSender
            do! Async.Sleep(int mp3Reader.reader.TotalTime.TotalMilliseconds)
        }

    let streamAudioClient filePath =
        async {
            ()
        }

    member val AudioSource : AudioSource = null with get, set

    override this.OnDestroy() =
        base.OnDestroy()
        ()

    /// Stream audio from the player (can be host or non-host) to all other players.
    member this.StreamAudioFromFile(filePath) =
        if this.IsHost then
            streamAudioHost filePath
        else streamAudioHost filePath
            // streamAudioClient filePath

    /// Initialize clients by setting the pcm header on each client.
    [<ClientRpc>]
    member this.InitializeAudioClientRpc(pcmHeader: PcmHeader) =
        if not this.IsHost then
            iter dispose audioReceiver
            audioReceiver <- Some <| AudioReceiver this.AudioSource pcmHeader

    /// Send the current frame data to each client.
    [<ClientRpc(Delivery = RpcDelivery.Unreliable)>]
    member this.SendFrameClientRpc(frameData) =
        if not this.IsHost then
            onReceiveFrame audioReceiver.Value frameData
            if not this.AudioSource.isPlaying then this.AudioSource.Play()