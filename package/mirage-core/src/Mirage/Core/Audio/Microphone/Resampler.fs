module Mirage.Core.Audio.Microphone.Resampler

#nowarn "40"

open IcedTasks
open System
open System.Collections.Generic
open System.Buffers
open System.Threading
open NAudio.Wave
open NAudio.Dsp
open Mirage.Core.Audio.PCM
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Task.Utility

let [<Literal>] private SampleRate = 16000
let [<Literal>] private BufferSize = 2000
let private WriterFormat = WaveFormat(SampleRate, 1)

/// Resamples the given audio samples using the resampler's configured in/out sample rates.<br />
/// This assumes the input/output is mono-channel audio.<br />
/// Source: https://markheath.net/post/fully-managed-input-driven-resampling-wdl
let inline private resample (resampler: WdlResampler) (samples: Samples) =
    let mutable inBuffer = null
    let mutable inBufferOffset = 0
    let inAvailable = resampler.ResamplePrepare(samples.length, 1, &inBuffer, &inBufferOffset)
    Buffer.BlockCopy(samples.data, 0, inBuffer, inBufferOffset, inAvailable * sizeof<float32>)
    let mutable outBuffer = ArrayPool.Shared.Rent BufferSize
    let outAvailable = resampler.ResampleOut(outBuffer, 0, inAvailable, BufferSize, 1)
    struct (outBuffer, outAvailable)

[<Struct>]
type AudioFrame =
    {   samples: Samples
        format: WaveFormat
    }

[<Struct>]
type ResampledAudio =
    {   original: AudioFrame
        resampled: AudioFrame
    }

[<Struct>]
type ResamplerInput<'State>
    = ResamplerInput of ValueTuple<'State, AudioFrame>
    | Reset

[<Struct>]
type ResamplerOutput<'State>
    = ResamplerOutput of ValueTuple<'State, ResampledAudio>
    | Reset

/// A live resampler for a microphone's input.
type Resampler<'State> = private { channel: Channel<ResamplerInput<'State>> }

let Resampler<'State> samplesPerWindow (onResampled: ResamplerOutput<'State> -> unit) =
    let windowDuration = float samplesPerWindow / float SampleRate
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    let channel = Channel CancellationToken.None
    let originalSamples = new List<float32>()
    let resampledSamples = new List<float32>()
    let consumer () =
        forever <| fun () -> valueTask {
            let! action = readChannel channel
            match action with
                | ResamplerInput.Reset ->
                    originalSamples.Clear()
                    resampledSamples.Clear()
                    onResampled Reset
                | ResamplerInput struct (state, frame) ->
                    try
                        let frameSamples = ArraySegment(frame.samples.data, 0, frame.samples.length)
                        originalSamples.AddRange frameSamples
                        if frame.format.SampleRate = SampleRate then
                            resampledSamples.AddRange frameSamples
                        else
                            resampler.SetRates(frame.format.SampleRate, SampleRate)
                            let struct (samples, sampleCount) = resample resampler frame.samples
                            try resampledSamples.AddRange <| ArraySegment(samples, 0, sampleCount)
                            finally ArrayPool.Shared.Return samples
                        let sampleSize = int <| windowDuration * float frame.format.SampleRate
                        if originalSamples.Count >= sampleSize && resampledSamples.Count >= samplesPerWindow then
                            let original = ArrayPool.Shared.Rent sampleSize
                            originalSamples.CopyTo(0, original, 0, sampleSize)
                            originalSamples.RemoveRange(0, sampleSize)
                            let resampled = ArrayPool.Shared.Rent samplesPerWindow
                            resampledSamples.CopyTo(0, resampled, 0, samplesPerWindow)
                            resampledSamples.RemoveRange(0, samplesPerWindow)
                            let resampledAudio =
                                {   original =
                                        {   samples = { data = original; length = sampleSize }
                                            format = frame.format
                                        }
                                    resampled =
                                        {   samples = { data = resampled; length = samplesPerWindow }
                                            format = WriterFormat
                                        }
                                }
                            onResampled << ResamplerOutput <| struct (state, resampledAudio)
                    finally
                        ArrayPool.Shared.Return frame.samples.data
        }
    fork CancellationToken.None consumer
    { channel = channel }

/// Add audio samples to be processed by the resampler.
/// This assumes the array is rented from __ArrayPool.Shared__, and will be returned after being used.
let writeResampler resampler = writeChannel resampler.channel