module Mirage.Hook.RecordAudio

#nowarn "40"

open NAudio.Wave
open System
open System.Collections.Generic
open Silero.API
open FSharpPlus
open FSharpx.Control
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.Resampler
open Mirage.Domain.Logger

let [<Literal>] private SampleRate = 16000
let [<Literal>] private SamplesPerWindow = 1536

let private resampler = Resampler()
let private silero = SileroVAD()
let private speechDetector =
    SpeechDetector (result << detectSpeech silero) <| fun detection ->
        match detection with
            | SpeechStart -> result <| logInfo "speech started"
            | SpeechEnd -> result <| logInfo "speech ended"
            | SpeechFound _ -> result ()

type AudioFrame =
    private
        {   samples: float32[]
            format: WaveFormat
        }

let channel =
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
                buffer.Clear()
            do! consumer
        }
    Async.Start consumer
    agent

type private MicrophoneSubscriber() =
    interface Dissonance.Audio.Capture.IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format: WaveFormat) =
            channel.Add { samples = buffer.Array; format = format }
        member _.Reset() = ()

let recordAudio () =
    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber()
    )