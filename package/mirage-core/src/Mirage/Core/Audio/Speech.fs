module Mirage.Core.Audio.Speech

open System
open FSharpx.Control
open Mirage.Prelude
open Mirage.Core.Async.LVar
open FSharpPlus

let [<Literal>] StartThreshold = 0.6f
let [<Literal>] EndThreshold = 0.45f
let [<Literal>] SamplingRate = 16000
let [<Literal>] MinSilenceDurationMs = 600
let [<Literal>] SpeechPadMs = 500
let MinSilenceSamples = float32 SamplingRate * float32 MinSilenceDurationMs / 1000f
let SpeechPadSamples = float32 SamplingRate * float32 SpeechPadMs / 1000f

type SpeechDetection
    = SpeechStart
    | SpeechFound of float32[]
    | SpeechEnd

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
    private { agent: BlockingQueueAgent<float32[]> }
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
    let agent = new BlockingQueueAgent<float32[]>(Int32.MaxValue)
    let speechDetector = { agent = agent }
    let consumer =
        async {
            let mutable currentSample = 0
            let mutable endSamples = 0
            let mutable speechDetected = false
            while true do
                let! samples = agent.AsyncGet()
                &currentSample += samples.Length
                let! probability = detectSpeech samples
                if probability >= StartThreshold then
                    if endSamples <> 0 then
                        endSamples <- 0
                    if not speechDetected then
                        speechDetected <- true
                        do! onSpeechDetected SpeechStart
                    do! onSpeechDetected <| SpeechFound samples
                else if probability < EndThreshold && speechDetected then
                    if endSamples = 0 then
                        endSamples <- currentSample
                    if float32 (currentSample - endSamples) < MinSilenceSamples then
                        do! onSpeechDetected <| SpeechFound samples
                    else
                        endSamples <- 0
                        speechDetected <- false
                        do! onSpeechDetected <| SpeechFound samples
                        do! onSpeechDetected SpeechEnd
        }
    Async.Start consumer
    speechDetector

/// Add audio samples to be processed by the speech detector.
let writeSamples speechDetector = speechDetector.agent.AsyncAdd