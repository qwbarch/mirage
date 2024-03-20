module Mirage.Utilities.Audio.Speech

open System
open System.Threading
open FSharpx.Control
open Mirage.Utilities.Operator

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

/// Detect if speech is found. All async functions are run on a separate thread.
type SpeechDetector =
    {   /// A function that detects if speech is found.<br />
        /// Assumes the given pcm data is 16khz, and contains 30ms of audio.
        ///
        /// Return value is a float32 in the range of 0f-1f.<br />
        /// The closer to 0f, the less likely speech is detected.
        /// The closer to 1f, the more likely speech is detected.
        detectSpeech: float32[] -> Async<float32>
        /// A function that gets called when speech is detected.
        onSpeechDetected: SpeechDetection -> Async<unit>
        /// Token source for cancelling any async computations.
        canceller: CancellationTokenSource
    }

/// <summary>
/// Initialize a vad detector by providing a vad algorithm, an action to
/// perform when speech is detected, as well as a source to read samples from.
/// </summary>
/// <returns>
/// A producer function that should be invoked every time audio data is available.<br />
/// This assumes the given audio data is 16khz and contains 30ms of audio.
/// </returns>
let initSpeechDetector speechDetector : float32[] -> unit =
    let agent = new BlockingQueueAgent<float32[]>(Int32.MaxValue)
    let consumer =
        async {
            let mutable currentSample = 0
            let mutable endSamples = 0
            let mutable speechDetected = false
            while true do
                let! samples = agent.AsyncGet()
                &currentSample += samples.Length
                let! probability = speechDetector.detectSpeech samples
                if probability >= StartThreshold then
                    if endSamples <> 0 then
                        endSamples <- 0
                    if not speechDetected then
                        speechDetected <- true
                        return! speechDetector.onSpeechDetected SpeechStart
                    return! speechDetector.onSpeechDetected <| SpeechFound samples
                else if probability < EndThreshold && speechDetected then
                    if endSamples = 0 then
                        endSamples <- currentSample
                    if float32 (currentSample - endSamples) < MinSilenceSamples then
                        return! speechDetector.onSpeechDetected <| SpeechFound samples
                    else
                        endSamples <- 0
                        speechDetected <- false
                        return! speechDetector.onSpeechDetected <| SpeechFound samples
                        return! speechDetector.onSpeechDetected SpeechEnd
        }
    Async.Start(consumer, speechDetector.canceller.Token)
    agent.Add