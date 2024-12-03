module Mirage.Domain.Audio.Receiver

open FSharpPlus
open FSharp.Control.Tasks.Affine.Unsafe
open UnityEngine
open System
open System.Threading
open OpusDotNet
open Mirage.PluginInfo
open Mirage.Prelude
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Codec
open Mirage.Core.Ply.Channel
open Mirage.Core.Ply.Fork
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
            decoderChannel: Channel<OpusPacket>
            playbackChannel: Channel<DecodedPacket>
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
        decoderChannel = Channel()
        playbackChannel = Channel()
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
    let rec decoderThread () =
        uply {
            let! opusPacket = readChannel receiver.decoderChannel receiver.cancellationToken
            let pcmData = Array.zeroCreate<byte> <| PacketPcmLength
            ignore <| receiver.decoder.Decode(opusPacket.opusData, opusPacket.opusData.Length, pcmData, PacketPcmLength)
            writeChannel receiver.playbackChannel
                {   samples = fromPCMBytes pcmData
                    sampleIndex = opusPacket.sampleIndex
                }
            do! decoderThread()
        }
    let rec playbackThread () =
        uply {
            if not receiver.disposed then
                let! decodedPacket = readChannel receiver.playbackChannel receiver.cancellationToken
                if decodedPacket.samples.Length > 0 then
                    &receiver.bufferedPackets += 1
                    ignore <| receiver.audioSource.clip.SetData(decodedPacket.samples, decodedPacket.sampleIndex)
                    receiver.onPacketDecoded decodedPacket
                if not receiver.audioSource.isPlaying && receiver.bufferedPackets >= receiver.minimumBufferedPackets then
                    receiver.audioSource.Play()
                do! playbackThread()
        }
    fork decoderThread receiver.cancellationToken
    ignore <| playbackThread()

/// This should be called when an opus packet is received. It will be internally decoded and played back via the audio source.
let onReceivePacket receiver opusPacket =
    if Option.isSome receiver then
        writeChannel receiver.Value.decoderChannel opusPacket