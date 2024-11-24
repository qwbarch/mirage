module Mirage.Domain.Audio.Receiver

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open UnityEngine
open NAudio.Wave
open Mirage.PluginInfo
open Mirage.Core.Audio.PCM
open Mirage.Domain.Audio.Frame

/// Receive audio from <b>AudioSender</b>.
type AudioReceiver =
    private
        {   audioSource: AudioSource
            pcmHeader: PcmHeader
            decompressor: IMp3FrameDecompressor
            // int represents the sampleIndex
            onFrameDecompressed: Samples -> int -> Unit
            decompressChannel: BlockingQueueAgent<FrameData>
            playbackChannel: BlockingQueueAgent<ValueTuple<Samples, int>>
            mutable disposed: bool
        }
    
    interface IDisposable with
        member this.Dispose() =
            try
                if not this.disposed then
                    this.disposed <- true
                    this.audioSource.Stop()
                    dispose this.decompressor
                    dispose this.decompressChannel
                    dispose this.playbackChannel
            finally
                UnityEngine.Object.Destroy this.audioSource.clip
                this.audioSource.clip <- null

/// Start receiving audio data from the server, and playing it back live.
/// 
/// Note: This will not stop the <b>AudioSource</b> if it's currently playing.
/// You will need to handle that yourself at the callsite.
let AudioReceiver (audioSource: AudioSource) pcmHeader onFrameDecompressed cancellationToken =
    audioSource.clip <-
        AudioClip.Create(
            pluginId,
            pcmHeader.samples,
            pcmHeader.channels,
            pcmHeader.frequency,
            false
        )
    ignore <| audioSource.clip.SetData(Array.zeroCreate(pcmHeader.samples * pcmHeader.channels), 0)
    let waveFormat =
        Mp3WaveFormat(
            pcmHeader.frequency,
            pcmHeader.channels,
            pcmHeader.blockSize,
            pcmHeader.bitRate
        )

    let playbackChannel = new BlockingQueueAgent<ValueTuple<Samples, int>>(Int32.MaxValue)
    let rec playbackThread =
        async {
            let! struct (samples, sampleIndex) = playbackChannel.AsyncGet()
            if samples.Length > 0 then
                ignore <| audioSource.clip.SetData(samples, sampleIndex)
                onFrameDecompressed samples sampleIndex
            if not audioSource.isPlaying then
                audioSource.Play()
            do! playbackThread
        }
    Async.StartImmediate(playbackThread, cancellationToken)

    let decompressor = new AcmMp3FrameDecompressor(waveFormat)
    let decompressChannel = new BlockingQueueAgent<FrameData>(Int32.MaxValue)
    let rec decompressThread =
        async {
            let! frameData = decompressChannel.AsyncGet()
            let samples = decompressFrame decompressor frameData.rawData
            do! playbackChannel.AsyncAdd <| struct (samples, frameData.sampleIndex)
            do! decompressThread
        }
    Async.Start(decompressThread, cancellationToken)

    {   audioSource = audioSource
        pcmHeader = pcmHeader
        decompressor = decompressor
        disposed = false
        decompressChannel = decompressChannel
        playbackChannel = playbackChannel
        onFrameDecompressed = onFrameDecompressed
    }

/// Set the audio receiver frame data, and play it if the audio source hasn't started yet.
let onReceiveFrame frameData = function
    | None -> ()
    | Some (receiver: AudioReceiver) ->
        Async.StartImmediate <| receiver.decompressChannel.AsyncAdd frameData