module Mirage.Core.Audio.Microphone.Resampler

#nowarn "40"

open Collections.Pooled
open IcedTasks
open System
open System.Buffers
open System.Threading
open NAudio.Wave
open NAudio.Dsp
open Mirage.Core.Audio.PCM
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Task.Utility

let [<Literal>] private SamplesPerWindow = 512
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

let Resampler<'State> (onResampled: ResamplerOutput<'State> -> unit) =
    let windowDuration = float SamplesPerWindow / float SampleRate
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    let channel = Channel CancellationToken.None
    let buffer =
        {|  original = new PooledList<float32>(ClearMode.Never)
            resampled = new PooledList<float32>(ClearMode.Never)
        |}
    let consumer () =
        forever <| fun () -> valueTask {
            let! action = readChannel channel
            match action with
                | ResamplerInput.Reset ->
                    buffer.original.Clear()
                    buffer.resampled.Clear()
                    onResampled Reset
                | ResamplerInput struct (state, frame) ->
                    try
                        let frameSamples = ArraySegment(frame.samples.data, 0, frame.samples.length)
                        appendSegment buffer.original frameSamples
                        if frame.format.SampleRate = SampleRate then
                            appendSegment buffer.resampled frameSamples
                        else
                            resampler.SetRates(frame.format.SampleRate, SampleRate)
                            let struct (samples, sampleCount) = resample resampler frame.samples
                            try appendSegment buffer.resampled <| ArraySegment(samples, 0, sampleCount)
                            finally ArrayPool.Shared.Return samples
                        let sampleSize = int <| windowDuration * float frame.format.SampleRate
                        if buffer.original.Count >= sampleSize && buffer.resampled.Count >= SamplesPerWindow then
                            let original = ArrayPool.Shared.Rent sampleSize
                            copyFrom buffer.original original sampleSize
                            buffer.original.RemoveRange(0, sampleSize)
                            let resampled = ArrayPool.Shared.Rent SamplesPerWindow
                            copyFrom buffer.resampled resampled SamplesPerWindow
                            buffer.resampled.RemoveRange(0, SamplesPerWindow)
                            let resampledAudio =
                                {   original =
                                        {   samples = { data = original; length = sampleSize }
                                            format = frame.format
                                        }
                                    resampled =
                                        {   samples = { data = resampled; length = SamplesPerWindow }
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