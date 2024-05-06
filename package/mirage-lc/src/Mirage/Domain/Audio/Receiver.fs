module Mirage.Domain.Audio.Receiver

open System
open FSharpPlus
open UnityEngine
open NAudio.Wave
open Mirage.PluginInfo
open Mirage.Domain.Audio.Frame

/// <summary>
/// Receive audio from <b>AudioSender</b>.
/// </summary>
type AudioReceiver =
    private
        {   audioSource: AudioSource
            pcmHeader: PcmHeader
            decompressor: IMp3FrameDecompressor
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

/// <summary>
/// Start receiving audio data from the server, and playing it back live.
/// 
/// Note: This will not stop the <b>AudioSource</b> if it's currently playing.
/// You will need to handle that yourself at the callsite.
/// </summary>
let AudioReceiver (audioSource: AudioSource) (pcmHeader: PcmHeader) : AudioReceiver =
    audioSource.clip <-
        AudioClip.Create(
            pluginId,
            pcmHeader.samples,
            pcmHeader.channels,
            pcmHeader.frequency,
            false
        )
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
    }

/// <summary>
/// Set the audio receiver frame data, and play it if the audio source hasn't started yet.
/// </summary>
let onReceiveFrame (receiver: AudioReceiver) (frameData: FrameData) =
    if not <| isNull receiver.audioSource.clip && not receiver.disposed then
        // TODO: decompress frame in separate thread.
        let pcmData = decompressFrame receiver.decompressor frameData.rawData
        if pcmData.Length > 0 then
            ignore <| receiver.audioSource.clip.SetData(pcmData, frameData.sampleIndex)
        if not receiver.audioSource.isPlaying then
            receiver.audioSource.Play()