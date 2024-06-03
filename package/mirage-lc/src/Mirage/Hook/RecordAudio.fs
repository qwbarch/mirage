module Mirage.Hook.RecordAudio

#nowarn "40"

open System
open System.IO
open System.Diagnostics
open System.Collections.Generic
open Silero.API
open NAudio.Wave
open NAudio.Lame
open FSharpPlus
open FSharpx.Control
open Mirage.Domain.Logger
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.Resampler
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Hook.VoiceRecognition

// TODO: This should only need to be declared in one area. Right now it's in multiple files.
let private baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)

let [<Literal>] private SampleRate = 16000
let [<Literal>] private SamplesPerWindow = 1024
let [<Literal>] private WriterPreset = LAMEPreset.STANDARD
let private WriterFormat = WaveFormat(SampleRate, 1)

let private resampler = Resampler()
let private silero = SileroVAD SamplesPerWindow
let private speechDetector =
    let mutable writer = None
    SpeechDetector (result << detectSpeech silero) <| fun speech ->
        async {
            match speech with
                | SpeechStart _ ->
                    logInfo "speech start"
                    let directory = Path.Join(baseDirectory, "Mirage")
                    let! mp3Writer = createMp3Writer directory WriterFormat WriterPreset
                    writer <- Some mp3Writer
                    do! transcribeSpeech speech mp3Writer
                | SpeechEnd _ ->
                    logInfo "speech end"
                    do! closeMp3Writer writer.Value
                    do! transcribeSpeech speech writer.Value
                    writer <- None
                | SpeechFound (_, samples) ->
                    do! writeMp3File writer.Value samples
                    do! transcribeSpeech speech writer.Value
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
                do! writeSamples speechDetector samples WriterFormat // TODO: This should be frame.format when the audio samples is no longer resampled.
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