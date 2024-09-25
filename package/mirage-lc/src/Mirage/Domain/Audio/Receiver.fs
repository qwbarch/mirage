module Mirage.Domain.Audio.Receiver

open System
open FSharpPlus
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
            mutable disposed: bool
        }
    
    interface IDisposable with
        member this.Dispose() =
            if not this.disposed then
                this.disposed <- true
                this.audioSource.Stop()
                UnityEngine.Object.Destroy this.audioSource.clip
                this.audioSource.clip <- null
                dispose this.decompressor

/// Start receiving audio data from the server, and playing it back live.
/// 
/// Note: This will not stop the <b>AudioSource</b> if it's currently playing.
/// You will need to handle that yourself at the callsite.
let AudioReceiver (audioSource: AudioSource) pcmHeader onFrameDecompressed =
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
        new Mp3WaveFormat(
            pcmHeader.frequency,
            pcmHeader.channels,
            pcmHeader.blockSize,
            pcmHeader.bitRate
        )
    {   audioSource = audioSource
        pcmHeader = pcmHeader
        decompressor = new AcmMp3FrameDecompressor(waveFormat)
        disposed = false
        onFrameDecompressed = onFrameDecompressed
    }

/// Set the audio receiver frame data, and play it if the audio source hasn't started yet.
let onReceiveFrame receiver frameData =
    if not <| isNull receiver.audioSource.clip then
        // TODO: decompress frame in separate thread.
        let samples = decompressFrame receiver.decompressor frameData.rawData
        if samples.Length > 0 then
            ignore <| receiver.audioSource.clip.SetData(samples, frameData.sampleIndex)
            receiver.onFrameDecompressed samples frameData.sampleIndex
        if not receiver.audioSource.isPlaying then
            receiver.audioSource.Play()