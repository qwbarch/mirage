module Mirage.Core.Audio.Microphone.Detection

open Collections.Pooled
open System
open System.Threading
open System.Buffers
open IcedTasks
open Mirage.Prelude
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Task.Loop
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Pooled

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
type DetectEnd =
    {   audioDurationMs: int
        /// Samples containing the entirety of speech being detected.
        fullAudio: ResampledAudio
    }

/// A sum type representing when speech is found or not.
[<Struct>]
type DetectAction
    = DetectStart of detectStart: DetectStart
    | DetectEnd of detectEnd: DetectEnd

/// Detect if speech is found. All async functions are run on a separate thread.
type VoiceDetector<'State> = { channel: Channel<ResamplerOutput<'State>> }

type VoiceDetectorArgs<'State> =
    {   /// Minimum amount of silence (in milliseconds) before VAD should stop.
        minSilenceDurationMs: int
        /// Uses the forced probability instead of using __detectSpeech__ for the probability,
        /// if the value is __Some probability__.
        forcedProbability: 'State -> ValueOption<float32>
        /// Minimum probability required to count as voice detection started.
        startThreshold: float32
        /// Maximum probability required to count as voice detection ended.
        endThreshold: float32
        /// A function that detects if speech is found.<br />
        /// Assumes the given data is 16khz sample rate, and contains 30ms of audio.
        ///
        /// Return value is a float32 in the range of 0f-1f.<br />
        /// The closer to 0f, the less likely speech is detected.
        /// The closer to 1f, the more likely speech is detected.
        /// 
        /// The int argument is the sampleCount, assuming the given samples are rented by __ArrayPool.Shared__.
        detectSpeech: Samples -> int -> float32
        /// Function that gets called every time a voice is detected, represented by __DetectAction__.
        onVoiceDetected: 'State -> DetectAction -> unit
    }

/// Initialize a vad detector by providing a vad algorithm, an action to
/// perform when speech is detected, as well as a source to read samples from.
let VoiceDetector args =
    let minSilenceSamples = float32 SamplingRate * float32 args.minSilenceDurationMs / 1000f
    let channel = Channel CancellationToken.None
    let samples =
        {|  original = new PooledList<float32>(ClearMode.Never)
            resampled = new PooledList<float32>(ClearMode.Never)
        |}
    let mutable currentIndex = 0
    let mutable endIndex = 0
    let mutable voiceDetected = false

    let consumer () =
        forever <| fun () -> valueTask {
            let! action = readChannel channel
            match action with
                | Reset ->
                    samples.original.Clear()
                    samples.resampled.Clear()
                    currentIndex <- 0
                    endIndex <- 0
                    voiceDetected <- false
                | ResamplerOutput struct (state, currentAudio) ->
                    try
                        &currentIndex += currentAudio.original.samples.length
                        appendSegment samples.original <| ArraySegment(currentAudio.original.samples.data, 0, currentAudio.original.samples.length)
                        appendSegment samples.resampled <| ArraySegment(currentAudio.resampled.samples.data, 0, currentAudio.resampled.samples.length)
                        let probability =
                            match args.forcedProbability state with
                                | ValueSome probability -> probability
                                | ValueNone -> args.detectSpeech currentAudio.resampled.samples currentAudio.resampled.samples.length
                        if probability >= args.startThreshold then
                            if endIndex <> 0 then
                                endIndex <- 0
                            if not voiceDetected then
                                voiceDetected <- true
                                args.onVoiceDetected state << DetectStart <|
                                    {   originalFormat = currentAudio.original.format
                                        resampledFormat = currentAudio.resampled.format
                                    }
                        else if probability < args.endThreshold && voiceDetected then
                            if endIndex = 0 then
                                endIndex <- currentIndex
                            if float32 (currentIndex - endIndex) >= minSilenceSamples then
                                let fullAudio =
                                    let original =
                                        let buffer = ArrayPool.Shared.Rent samples.original.Count
                                        copyFrom samples.original buffer samples.original.Count
                                        buffer
                                    let resampled =
                                        let buffer = ArrayPool.Shared.Rent samples.resampled.Count
                                        copyFrom samples.resampled buffer samples.resampled.Count
                                        buffer
                                    {   original =
                                            {   format = currentAudio.original.format
                                                samples = { data = original; length = samples.original.Count }
                                            }
                                        resampled =
                                            {   format = currentAudio.resampled.format
                                                samples = { data = resampled; length = samples.resampled.Count }
                                            }
                                    }
                                endIndex <- 0
                                voiceDetected <- false
                                args.onVoiceDetected state << DetectEnd <|
                                    {   audioDurationMs = audioLengthMs fullAudio.original.format samples.original.Count
                                        fullAudio = fullAudio
                                    }
                                samples.original.Clear()
                                samples.resampled.Clear()
                        else if not voiceDetected then
                            samples.original.Clear()
                            samples.resampled.Clear()
                    finally
                        ArrayPool.Shared.Return currentAudio.original.samples.data
                        ArrayPool.Shared.Return currentAudio.resampled.samples.data
        }
    fork CancellationToken.None consumer
    { channel = channel }

/// Add audio samples to be processed by the voice detector.
let inline writeDetector detector = writeChannel detector.channel