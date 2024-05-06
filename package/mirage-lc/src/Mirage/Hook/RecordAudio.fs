module Mirage.Hook.RecordAudio

#nowarn "40"

open System
open System.Collections.Generic
open Silero.API
open NAudio.Wave
open NAudio.Lame
open FSharpPlus
open FSharpx.Control
open UnityEngine
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.Resampler
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Domain.Logger

let [<Literal>] private SampleRate = 16000
let [<Literal>] private SamplesPerWindow = 1024
let [<Literal>] private WriterPreset = LAMEPreset.STANDARD
let private WriterFormat = WaveFormat(SampleRate, 1)

let private resampler = Resampler()
let private silero = SileroVAD SamplesPerWindow
let private speechDetector =
    let mutable writer = None
    SpeechDetector (result << detectSpeech silero) <| fun detection ->
        async {
            match detection with
                | SpeechStart ->
                    logInfo "speech start"
                    let filePath = $"{Application.dataPath}/../Mirage/{DateTime.UtcNow.ToFileTime()}.mp3"
                    let! mp3Writer = createMp3Writer filePath WriterFormat WriterPreset
                    writer <- Some mp3Writer
                | SpeechEnd ->
                    logInfo "speech end"
                    do! closeMp3Writer writer.Value
                    writer <- None
                | SpeechFound samples -> do! writeMp3File writer.Value samples
        }

type AudioFrame =
    private
        {   samples: float32[]
            format: WaveFormat
        }

let private channel =
    let agent = new BlockingQueueAgent<AudioFrame>(Int32.MaxValue)
    let buffer = new List<float32>()
    let rec consumer =
        async {
            let! frame = agent.AsyncGet()
            buffer.AddRange <|
                if frame.format.SampleRate <> SampleRate then
                    setRates resampler frame.format.SampleRate SampleRate
                    resample resampler frame.samples
                else
                    frame.samples
            if buffer.Count >= SamplesPerWindow then
                let samples = buffer.GetRange(0, SamplesPerWindow).ToArray()
                buffer.RemoveRange(0, SamplesPerWindow)
                do! writeSamples speechDetector samples
            do! consumer
        }
    Async.Start consumer
    agent

type private MicrophoneSubscriber() =
    interface Dissonance.Audio.Capture.IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            channel.Add {
                samples = buffer.ToArray()
                format = WaveFormat(format.SampleRate, format.Channels)
            }
        member _.Reset() = ()

let recordAudio () =
    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber()
    )