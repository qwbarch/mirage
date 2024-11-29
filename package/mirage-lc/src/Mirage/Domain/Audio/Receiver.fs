module Mirage.Domain.Audio.Receiver

#nowarn "40"

open Concentus
open Concentus.Structs
open FSharpPlus
open FSharpx.Control
open UnityEngine
open System
open System.Threading
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Codec
open Mirage.PluginInfo
open Mirage.Domain.Audio.Packet

[<Struct>]
type DecodedPacket =
    {   samples: Samples
        sampleIndex: int
    }

type AudioReceiverArgs =
    {   audioSource: AudioSource
        pcmHeader: PcmHeader
        onPacketDecoded: DecodedPacket -> Unit
        cancellationToken: CancellationToken
    }

type AudioReceiver =
    private
        {   audioSource: AudioSource
            pcmHeader: PcmHeader
            decoder: IOpusDecoder
            onPacketDecoded: DecodedPacket -> Unit
            cancellationToken: CancellationToken
            decoderChannel: BlockingQueueAgent<OpusPacket>
            playbackChannel: BlockingQueueAgent<DecodedPacket>
            mutable disposed: bool
        }
    interface IDisposable with
        member this.Dispose () =
            try
                if not this.disposed then
                    this.disposed <- true
                    this.audioSource.Stop()
                    dispose this.decoder
                    dispose this.decoderChannel
                    dispose this.playbackChannel
            finally
                UnityEngine.Object.Destroy this.audioSource.clip
                this.audioSource.clip <- null

/// The inverse of __AudioSender__. Receives packets sent by the AudioSender, decodes the opus packet, and then plays it back live.
let AudioReceiver args =
    {   audioSource = args.audioSource
        pcmHeader = args.pcmHeader
        decoder = OpusDecoder()
        onPacketDecoded = args.onPacketDecoded
        cancellationToken = args.cancellationToken
        decoderChannel = new BlockingQueueAgent<OpusPacket>(Int32.MaxValue)
        playbackChannel = new BlockingQueueAgent<DecodedPacket>(Int32.MaxValue)
        disposed = false
    }

/// Start receiving audio data from the server, and playing it back live.
/// 
/// Note: This will not stop the <b>AudioSource</b> if it's currently playing.
/// You will need to handle that yourself at the callsite.
let startAudioReceiver receiver =
    receiver.audioSource.clip <-
        AudioClip.Create(
            pluginId,
            receiver.pcmHeader.totalSamples,
            receiver.pcmHeader.channels,
            receiver.pcmHeader.sampleRate,
            false
        )
    let rec decoderThread =
        async {
            let! opusPacket = receiver.decoderChannel.AsyncGet()
            let packet = opusPacket.opusData.AsSpan()
            let frameSize = OpusPacketInfo.GetNumSamples(packet, OpusSampleRate)
            let samples = Array.zeroCreate<float32> frameSize
            ignore <| receiver.decoder.Decode(packet, samples, frameSize)
            do! receiver.playbackChannel.AsyncAdd <|
                {   samples = samples
                    sampleIndex = opusPacket.sampleIndex
                }
            do! decoderThread
        }
    let rec playbackThread =
        async {
            let! decodedPacket = receiver.playbackChannel.AsyncGet()
            if decodedPacket.samples.Length > 0 then
                ignore <| receiver.audioSource.clip.SetData(decodedPacket.samples, decodedPacket.sampleIndex)
            if not receiver.audioSource.isPlaying then
                receiver.audioSource.Play()
            do! playbackThread
        }
    Async.Start(decoderThread, receiver.cancellationToken)
    Async.StartImmediate(playbackThread, receiver.cancellationToken)