module Mirage.Core.Audio.Microphone.Detection

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Prelude
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler

let [<Literal>] private StartThreshold = 0.6f
let [<Literal>] private EndThreshold = 0.45f
let [<Literal>] private SamplingRate = 16000
let [<Literal>] private MinSilenceDurationMs = 600
let private MinSilenceSamples = float32 SamplingRate * float32 MinSilenceDurationMs / 1000f

type VADFrame =
    {   elapsedTime: int
        probability: float32
    }

type DetectStart =
    {   originalFormat: WaveFormat
        resampledFormat: WaveFormat
    }

type DetectFound =
    {   vadFrame: VADFrame
        /// Samples containing only the current audio frame.
        currentAudio: ResampledAudio
        /// Samples containing the entirety of speech being detected.
        fullAudio: ResampledAudio
    }

type DetectEnd =
    {   vadTimings: list<VADFrame>
        audioDurationMs: int
        /// Samples containing only the current audio frame.
        currentAudio: ResampledAudio
        /// Samples containing the entirety of speech being detected.
        fullAudio: ResampledAudio
    }

/// A sum type representing when speech is found or not.
type DetectAction
    = DetectStart of DetectStart
    | DetectFound of DetectFound
    | DetectEnd of DetectEnd

/// A function that detects if speech is found.<br />
/// Assumes the given data is 16khz sample rate, and contains 30ms of audio.
///
/// Return value is a float32 in the range of 0f-1f.<br />
/// The closer to 0f, the less likely speech is detected.
/// The closer to 1f, the more likely speech is detected.
type DetectVoice = Samples -> Async<float32>

/// Detect if speech is found. All async functions are run on a separate thread.
type VoiceDetector =
    private { agent: BlockingQueueAgent<ResampledAudio> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

/// Initialize a vad detector by providing a vad algorithm, an action to
/// perform when speech is detected, as well as a source to read samples from.
let VoiceDetector (detectSpeech: DetectVoice) (onVoiceDetected: DetectAction -> Async<Unit>) =
    let agent = new BlockingQueueAgent<ResampledAudio>(Int32.MaxValue)
    let samples =
        {|  original = new List<float32>()
            resampled = new List<float32>()
        |}
    let mutable vadFrames = []
    let mutable currentIndex = 0
    let mutable endIndex = 0
    let mutable voiceDetected = false
    let rec consumer =
        async {
            let! currentAudio = agent.AsyncGet()
            &currentIndex += currentAudio.original.samples.Length
            samples.original.AddRange currentAudio.original.samples
            samples.resampled.AddRange currentAudio.resampled.samples
            let! probability = detectSpeech currentAudio.resampled.samples
            let fullAudio =
                {   original =
                        {   format = currentAudio.original.format
                            samples = samples.original.ToArray()
                        }
                    resampled =
                        {   format = currentAudio.resampled.format
                            samples = samples.resampled.ToArray()
                        }
                }
            let vadFrame =
                {   elapsedTime = audioLengthMs fullAudio.original.format fullAudio.original.samples
                    probability = probability
                }
            let detectFound =
                DetectFound
                    {   vadFrame = vadFrame
                        currentAudio = currentAudio
                        fullAudio = fullAudio
                    }
            if probability >= StartThreshold then
                if endIndex <> 0 then
                    endIndex <- 0
                if not voiceDetected then
                    voiceDetected <- true
                    vadFrames <- [vadFrame]
                    do! onVoiceDetected << DetectStart <|
                        {   originalFormat = currentAudio.original.format
                            resampledFormat = currentAudio.resampled.format
                        }
                else
                    vadFrames <- vadFrame :: vadFrames
                do! onVoiceDetected detectFound
            else if probability < EndThreshold && voiceDetected then
                if endIndex = 0 then
                    endIndex <- currentIndex
                if float32 (currentIndex - endIndex) < MinSilenceSamples then
                    do! onVoiceDetected detectFound
                else
                    endIndex <- 0
                    voiceDetected <- false
                    do! onVoiceDetected <| detectFound
                    do! onVoiceDetected << DetectEnd <|
                        {   vadTimings = rev vadFrames
                            audioDurationMs = vadFrame.elapsedTime
                            currentAudio = currentAudio
                            fullAudio = fullAudio
                        }
                    vadFrames <- []
                    samples.original.Clear()
                    samples.resampled.Clear()
            else if not voiceDetected then
                samples.original.Clear()
                samples.resampled.Clear()
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

/// Add audio samples to be processed by the voice detector.
let writeDetector = _.agent.AsyncAdd