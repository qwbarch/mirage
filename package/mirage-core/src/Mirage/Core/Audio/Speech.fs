module Mirage.Core.Audio.Speech

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Prelude
open Mirage.Core.Audio.PCM

let [<Literal>] StartThreshold = 0.6f
let [<Literal>] EndThreshold = 0.45f
let [<Literal>] SamplingRate = 16000
let [<Literal>] MinSilenceDurationMs = 600
let [<Literal>] SpeechPadMs = 500
let MinSilenceSamples = float32 SamplingRate * float32 MinSilenceDurationMs / 1000f
let SpeechPadSamples = float32 SamplingRate * float32 SpeechPadMs / 1000f

type VADFrame =
    {   sampleIndex: int
        elapsedTime: int
        probability: float32
    }

/// A sum type representing when speech is found or not. <b>float32[]</b> always represents audio samples.
type SpeechDetection
    = SpeechStart
    | SpeechFound of VADFrame * float32[]
    | SpeechEnd of list<VADFrame> * float32[] * int // Final argument is the total audio duration in milliseconds.

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
            let mutable vadFrames = []
            let mutable sampleIndex = 0
            let mutable endSamples = 0
            let mutable speechDetected = false
            let sampleBuffer = new List<float32>()
            while true do
                let! (currentSamples, waveFormat) = agent.AsyncGet()
                &sampleIndex += currentSamples.Length
                sampleBuffer.AddRange currentSamples
                let! probability = detectSpeech currentSamples
                let samples = sampleBuffer.ToArray()
                let vadFrame =
                    {   sampleIndex = sampleBuffer.Count - 1
                        elapsedTime = audioLengthMs waveFormat samples
                        probability = probability
                    }
                if probability >= StartThreshold then
                    if endSamples <> 0 then
                        endSamples <- 0
                    if not speechDetected then
                        speechDetected <- true
                        vadFrames <- [vadFrame]
                        do! onSpeechDetected SpeechStart
                    else
                        vadFrames <- vadFrame :: vadFrames
                    do! onSpeechDetected <| SpeechFound(vadFrame, currentSamples)
                else if probability < EndThreshold && speechDetected then
                    if endSamples = 0 then
                        endSamples <- sampleIndex
                    if float32 (sampleIndex - endSamples) < MinSilenceSamples then
                        do! onSpeechDetected <| SpeechFound(vadFrame, currentSamples)
                    else
                        endSamples <- 0
                        speechDetected <- false
                        do! onSpeechDetected <| SpeechFound(vadFrame, currentSamples)
                        do! onSpeechDetected <| SpeechEnd(rev vadFrames, samples, vadFrame.elapsedTime)
                        sampleBuffer.Clear()
        }
    Async.Start consumer
    speechDetector

/// Add audio samples to be processed by the speech detector.
let writeSamples speechDetector = curry speechDetector.agent.AsyncAdd