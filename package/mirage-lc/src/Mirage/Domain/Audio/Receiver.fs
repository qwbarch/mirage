module Mirage.Domain.Audio.Receiver

#nowarn "40"

open UnityEngine
open System
open System.Threading
open Mirage.PluginInfo
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Logger
open Mirage.Core.Audio.PCM

type AudioReceiver =
    private
        {   audioSource: AudioSource
            cancellationToken: CancellationToken
            waveHeader: WaveHeader
            mutable disposed: bool
        }
    interface IDisposable with
        member this.Dispose () =
            try
                if not this.disposed then
                    this.disposed <- true
                    this.audioSource.Stop()
            finally
                UnityEngine.Object.Destroy this.audioSource.clip
                this.audioSource.clip <- null

/// The inverse of __AudioSender__. Receives packets sent by the AudioSender, decodes the opus packet, and then plays it back live.
let AudioReceiver audioSource waveHeader onPacketDecoded cancellationToken =
    {   audioSource = audioSource
        cancellationToken = cancellationToken
        waveHeader = waveHeader
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
            receiver.waveHeader.lengthSamples,
            receiver.waveHeader.channels,
            receiver.waveHeader.frequency,
            false
        )

/// This should be called when an opus packet is received. It will be internally decoded and played back via the audio source.
let onReceivePacket packet = function
    | None -> ()
    | Some receiver ->
        if not receiver.disposed then
            if not (isNull packet.pcmData) && packet.pcmData.Length > 0 then
                let samples = fromPCMBytes packet.pcmData
                logInfo $"samples length: {packet.pcmData.Length} sampleIndex: {packet.sampleIndex}"
                ignore <| receiver.audioSource.clip.SetData(samples, packet.sampleIndex)
            if not receiver.audioSource.isPlaying then
                receiver.audioSource.Play()