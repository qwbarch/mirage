module Mirage.Domain.Audio.Receiver

#nowarn "40"

open FSharpPlus
open FSharpx.Control
open UnityEngine
open System
open System.Threading
open OpusDotNet
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Codec
open Mirage.PluginInfo
open Mirage.Prelude
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Audio.Stream

[<Struct>]
type DecodedPacket =
    {   samples: Samples
        sampleIndex: int
    }

type AudioReceiver =
    private
        {   audioSource: AudioSource
            totalSamples: int
            decoder: OpusDecoder
            onPacketDecoded: DecodedPacket -> Unit
            cancellationToken: CancellationToken
            decoderChannel: BlockingQueueAgent<OpusPacket>
            playbackChannel: BlockingQueueAgent<DecodedPacket>
            minimumBufferedPackets: int
            mutable bufferedPackets: int
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
let AudioReceiver audioSource totalSamples onPacketDecoded cancellationToken =
    {   audioSource = audioSource
        totalSamples = totalSamples
        decoder = OpusDecoder()
        onPacketDecoded = onPacketDecoded
        cancellationToken = cancellationToken
        decoderChannel = new BlockingQueueAgent<OpusPacket>(Int32.MaxValue)
        playbackChannel = new BlockingQueueAgent<DecodedPacket>(Int32.MaxValue)
        minimumBufferedPackets =
            Math.Min(
                MinimumBufferedAudioMs / FrameSizeMs,
                int <| float totalSamples / (float OpusSampleRate * float FrameSizeMs / 1000.0)
            )
        bufferedPackets = 0
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
            receiver.totalSamples,
            OpusChannels,
            OpusSampleRate,
            false
        )
    let rec decoderThread =
        async {
            let! opusPacket = receiver.decoderChannel.AsyncGet()
            let pcmData = Array.zeroCreate<byte> <| PacketPcmLength
            ignore <| receiver.decoder.Decode(opusPacket.opusData, opusPacket.opusData.Length, pcmData, PacketPcmLength)
            do! receiver.playbackChannel.AsyncAdd <|
                {   samples = fromPCMBytes pcmData
                    sampleIndex = opusPacket.sampleIndex
                }
            do! decoderThread
        }
    let rec playbackThread =
        async {
            if not receiver.disposed then
                let! decodedPacket = receiver.playbackChannel.AsyncGet()
                if decodedPacket.samples.Length > 0 then
                    &receiver.bufferedPackets += 1
                    ignore <| receiver.audioSource.clip.SetData(decodedPacket.samples, decodedPacket.sampleIndex)
                    receiver.onPacketDecoded decodedPacket
                if not receiver.audioSource.isPlaying && receiver.bufferedPackets >= receiver.minimumBufferedPackets then
                    receiver.audioSource.Play()
                do! playbackThread
        }
    Async.Start(decoderThread, receiver.cancellationToken)
    Async.StartImmediate(playbackThread, receiver.cancellationToken)

/// This should be called when an opus packet is received. It will be internally decoded and played back via the audio source.
let onReceivePacket opusPacket = function
    | None -> ()
    | Some receiver ->
        Async.StartImmediate <| receiver.decoderChannel.AsyncAdd opusPacket