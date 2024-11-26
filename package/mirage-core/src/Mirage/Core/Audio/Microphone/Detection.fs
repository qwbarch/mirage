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

let [<Literal>] private StartThreshold = 0.7f
let [<Literal>] private EndThreshold = 0.45f
let [<Literal>] private SamplingRate = 16000

[<Struct>]
type VADFrame =
    {   elapsedTime: int
        probability: float32
    }

[<Struct>]
type DetectStart =
    {   originalFormat: WaveFormat
        resampledFormat: WaveFormat
    }

[<Struct>]
type DetectFound =
    {   vadFrame: VADFrame
        /// Samples containing only the current audio frame.
        currentAudio: ResampledAudio
        /// Samples containing the entirety of speech being detected.
        fullAudio: ResampledAudio
    }

type DetectEnd =
    {   /// VAD timing for the final frame.
        vadFrame: VADFrame
        vadTimings: list<VADFrame>
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

/// Detect if speech is found. All async functions are run on a separate thread.
type VoiceDetector<'State> =
    private { channel: BlockingQueueAgent<ResamplerOutput<'State>> }
    interface IDisposable with
        member this.Dispose() = dispose this.channel

type VoiceDetectorArgs<'State> =
    {   /// Minimum amount of silence (in milliseconds) before VAD should stop.
        minSilenceDurationMs: int
        /// Uses the forced probability instead of using __detectSpeech__ for the probability,
        /// if the value is __Some probability__.
        forcedProbability: 'State -> Option<float32>
        /// A function that detects if speech is found.<br />
        /// Assumes the given data is 16khz sample rate, and contains 30ms of audio.
        ///
        /// Return value is a float32 in the range of 0f-1f.<br />
        /// The closer to 0f, the less likely speech is detected.
        /// The closer to 1f, the more likely speech is detected.
        detectSpeech: Samples -> Async<float32>
        /// Function that gets called every time a voice is detected, represented by __DetectAction__.
        onVoiceDetected: 'State -> DetectAction -> Async<Unit>
    }

/// Initialize a vad detector by providing a vad algorithm, an action to
/// perform when speech is detected, as well as a source to read samples from.
let VoiceDetector<'State> args =
    let minSilenceSamples = float32 SamplingRate * float32 args.minSilenceDurationMs / 1000f
    let channel = new BlockingQueueAgent<ResamplerOutput<'State>>(Int32.MaxValue)
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
            do! channel.AsyncGet() >>= function
                | Reset ->
                    async {
                        samples.original.Clear()
                        samples.resampled.Clear()
                        vadFrames <- []
                        endIndex <- 0
                        voiceDetected <- false
                    }
                | ResamplerOutput struct (state, currentAudio) ->
                    async {
                        let onVoiceDetected = args.onVoiceDetected state
                        &currentIndex += currentAudio.original.samples.Length
                        samples.original.AddRange currentAudio.original.samples
                        samples.resampled.AddRange currentAudio.resampled.samples
                        let! probability =
                            match args.forcedProbability state with
                                | Some probability -> result probability
                                | None ->
                                    args.detectSpeech currentAudio.resampled.samples
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
                            if float32 (currentIndex - endIndex) < minSilenceSamples then
                                do! onVoiceDetected detectFound
                            else
                                endIndex <- 0
                                voiceDetected <- false
                                do! onVoiceDetected <| detectFound
                                do! onVoiceDetected << DetectEnd <|
                                    {   vadFrame = vadFrame
                                        vadTimings = rev vadFrames
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
            do! consumer
        }
    Async.Start consumer
    { channel = channel }

/// Add audio samples to be processed by the voice detector.
let writeDetector = _.channel.AsyncAdd