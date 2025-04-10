module Mirage.Domain.Audio.Receiver

open FSharpPlus
open UnityEngine
open System
open System.Threading
open System.Buffers
open OpusDotNet
open IcedTasks
open Mirage.Prelude
open Mirage.Core.Audio.PCM
open Mirage.Core.Task.Channel
open Mirage.Core.Audio.Opus.Codec
open Mirage.Core.Task.Loop
open Mirage.Core.Task.Fork
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Audio.Stream
open Mirage.Domain.Null

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
            if not this.disposed then
                this.disposed <- true
                dispose this.decoder
                if isNotNull this.audioSource then
                    this.audioSource.Stop()
                    if isNotNull this.audioSource.clip then
                        UnityEngine.Object.Destroy this.audioSource.clip
                        this.audioSource.clip <- null

/// The inverse of __AudioSender__. Receives packets sent by the AudioSender, decodes the opus packet, and then plays it back live.
let AudioReceiver audioSource totalSamples onPacketDecoded cancellationToken =
    {   audioSource = audioSource
        totalSamples = totalSamples
        decoder = OpusDecoder()
        onPacketDecoded = onPacketDecoded
        cancellationToken = cancellationToken
        decoderChannel = Channel cancellationToken
        playbackChannel = Channel cancellationToken
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
            "AudioReceiver",
            receiver.totalSamples,
            OpusChannels,
            OpusSampleRate,
            false
        )
    let decoderThread () =
        forever <| fun () -> valueTask {
            let! opusPacket = readChannel receiver.decoderChannel
            let pcmData = ArrayPool.Shared.Rent PacketPcmLength
            try
                ignore <| receiver.decoder.Decode(opusPacket.opusData, opusPacket.opusLength, pcmData, PacketPcmLength)
                writeChannel receiver.playbackChannel <|
                    {   samples = fromPcmData { data = pcmData; length = PacketPcmLength }
                        sampleIndex = opusPacket.sampleIndex
                    }
            finally
                ArrayPool.Shared.Return opusPacket.opusData
                ArrayPool.Shared.Return pcmData
        }
    let playbackThread () =
        valueTask {
            while not receiver.disposed do
                let! decodedPacket = readChannel receiver.playbackChannel
                try
                    if decodedPacket.samples.length > 0 then
                        // Unfortunately, AudioClip.SetData does not have the Span variation in the current Unity version.
                        // This is not ideal, but since packets now come in order and are guranteed to arrive,
                        // SetData() will not overwrite data due to the packet array being too large (from being rented).
                        &receiver.bufferedPackets += 1
                        ignore <| receiver.audioSource.clip.SetData(decodedPacket.samples.data, decodedPacket.sampleIndex)
                        receiver.onPacketDecoded decodedPacket
                    if not receiver.audioSource.isPlaying && receiver.bufferedPackets >= receiver.minimumBufferedPackets then
                        receiver.audioSource.Play()
                finally
                    ArrayPool.Shared.Return decodedPacket.samples.data
        }
    fork receiver.cancellationToken decoderThread
    ignore <| playbackThread()

/// This should be called when an opus packet is received. It will be internally decoded and played back via the audio source.
let onReceivePacket receiver opusPacket =
    if Option.isSome receiver then
        ignore <| writeChannel receiver.Value.decoderChannel opusPacket