module Mirage.Core.Audio.Speech

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Core.Audio.PCM
open Mirage.Prelude

let [<Literal>] StartThreshold = 0.6f
let [<Literal>] EndThreshold = 0.45f
let [<Literal>] SamplingRate = 16000
let [<Literal>] MinSilenceDurationMs = 600
let [<Literal>] SpeechPadMs = 500
let MinSilenceSamples = float32 SamplingRate * float32 MinSilenceDurationMs / 1000f
let SpeechPadSamples = float32 SamplingRate * float32 SpeechPadMs / 1000f

type SpeechDetection
    = SpeechStart of DateTime // Contains the current UTC time.
    | SpeechFound of Tuple<DateTime, float32[]> // Current UTC time, and an array containing the current frame of audio samples.
    | SpeechEnd of Tuple<float32[], list<DateTime>> // An array containing all audio samples, and a list containing VAD timings, relative to the starting UTC time.

/// A function that detects if speech is found.<br />
/// Assumes the given pcm data is 16khz, and contains 30ms of audio.
///
/// Return value is a float32 in the range of 0f-1f.<br />
/// The closer to 0f, the less likely speech is detected.
/// The closer to 1f, the more likely speech is detected.
type DetectSpeech = float32[] -> Async<float32>

/// A function that executes whenever speech is detected.
type OnSpeechDetected = SpeechDetection -> Async<unit>

/// Detect if speech is found. All async functions are run on a separate thread.
type SpeechDetector =
    private { agent: BlockingQueueAgent<Tuple<float32[], WaveFormat>> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

/// <summary>
/// Initialize a vad detector by providing a vad algorithm, an action to
/// perform when speech is detected, as well as a source to read samples from.
/// </summary>
/// <returns>
/// A producer function that should be invoked every time audio data is available.<br />
/// This assumes the given audio data is 16khz and contains 30ms of audio.
/// </returns>
let SpeechDetector (detectSpeech: DetectSpeech) (onSpeechDetected: OnSpeechDetected) =
    let agent = new BlockingQueueAgent<Tuple<float32[], WaveFormat>>(Int32.MaxValue)
    let speechDetector = { agent = agent }
    let consumer =
        async {
            let mutable startTime = None
            let mutable vadTimings = []
            let mutable sampleIndex = 0
            let mutable endSamples = 0
            let mutable speechDetected = false
            let samples = new List<float32>()
            while true do
                let! (currentSamples, waveFormat) = agent.AsyncGet()
                &sampleIndex += currentSamples.Length
                samples.AddRange currentSamples
                let! probability = detectSpeech currentSamples
                if probability >= StartThreshold then
                    if endSamples <> 0 then
                        endSamples <- 0
                    if not speechDetected then
                        speechDetected <- true
                        startTime <- Some DateTime.UtcNow
                        vadTimings <- [startTime.Value]
                        do! onSpeechDetected <| SpeechStart startTime.Value
                    else
                        let timing = startTime.Value.AddMilliseconds << float << audioLengthMs waveFormat <| samples.ToArray()
                        //printfn $"added ms: {audioLengthMs waveFormat <| samples.ToArray()}"
                        vadTimings <- timing :: vadTimings
                    do! onSpeechDetected <| SpeechFound(startTime.Value, currentSamples)
                else if probability < EndThreshold && speechDetected then
                    if endSamples = 0 then
                        endSamples <- sampleIndex
                    if float32 (sampleIndex - endSamples) < MinSilenceSamples then
                        do! onSpeechDetected <| SpeechFound(startTime.Value, currentSamples)
                    else
                        endSamples <- 0
                        speechDetected <- false
                        do! onSpeechDetected <| SpeechFound(startTime.Value, currentSamples)
                        do! onSpeechDetected <| SpeechEnd(samples.ToArray(), rev vadTimings)
        }
    Async.Start consumer
    speechDetector

/// Add audio samples to be processed by the speech detector.
let writeSamples speechDetector = curry speechDetector.agent.AsyncAdd