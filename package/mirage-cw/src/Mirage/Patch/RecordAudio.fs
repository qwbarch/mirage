module Mirage.Patch.RecordAudio

#nowarn "40"

open System
open System.Threading
open FSharpPlus
open FSharpx.Control
open HarmonyLib
open Zorro.Recorder
open Photon.Voice.PUN
open Photon.Voice.Unity
open Mirage.Core.Monad
open Mirage.Core.Audio.Format
open Mirage.Core.Logger
open WebRtcVadSharp
open NAudio.Wave
open Mirage.Core.Audio.Resampler
open Microsoft.FSharp.Core.Option

type MicrophoneAudio =
    private
        {   samples: float32[]
            sampleRate: int
            channels: int
        }

type RecordAudio() =
    static let mutable IsRecording = false
    static let mutable Format = null
    static let vad = new WebRtcVad()

    //https://markheath.net/post/fully-managed-input-driven-resampling-wdl

    static let audioChannel =
        let channel = new BlockingQueueAgent<MicrophoneAudio>(Int32.MaxValue)
        let mutable resampler = None
        let rec consumer =
            async {
                let! audio = channel.AsyncGet()
                let waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.sampleRate, audio.channels)
                if isNone resampler then
                    resampler <- Some <| defaultResampler audio.sampleRate 16000
                let pcmData = toPCMBytes <| resample resampler.Value audio.samples
                let speechDetected = vad.HasSpeech(pcmData, SampleRate.Is16kHz, FrameLength.Is20ms)
                logInfo $"speechDetected: {speechDetected}"
                return! consumer
            }
        let canceller = new CancellationTokenSource()
        forkAsync canceller.Token consumer
        channel

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<RecorderAudioListener>, "SendMic")>]
    static member ``record audio to disk``(buffer, sampleRate, channels) =
        audioChannel.Add
            {   samples = buffer
                sampleRate = sampleRate
                channels = channels
            }