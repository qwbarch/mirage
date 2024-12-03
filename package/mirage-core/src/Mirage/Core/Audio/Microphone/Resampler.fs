module Mirage.Core.Audio.Microphone.Resampler

#nowarn "40"

open System
open System.Collections.Generic
open System.Buffers
open FSharpPlus
open Ply
open FSharp.Control.Tasks.Affine.Unsafe
open NAudio.Wave
open NAudio.Dsp
open Mirage.Core.Audio.PCM
open Mirage.Core.Ply.Channel
open Mirage.Core.Ply.Fork

let [<Literal>] private SampleRate = 16000
let [<Literal>] private BufferSize = 2000
let private WriterFormat = WaveFormat(SampleRate, 1)

let resamplePool: ArrayPool<float32> = ArrayPool.Shared

/// Resamples the given audio samples using the resampler's configured in/out sample rates.<br />
/// This assumes the input/output is mono-channel audio.<br />
/// Source: https://markheath.net/post/fully-managed-input-driven-resampling-wdl
let private resample (resampler: WdlResampler) (samples: Samples) =
    let mutable inBuffer = null
    let mutable inBufferOffset = 0
    let inAvailable = resampler.ResamplePrepare(samples.Length, 1, &inBuffer, &inBufferOffset)
    Array.Copy(samples, 0, inBuffer, inBufferOffset, inAvailable)
    let mutable outBuffer = resamplePool.Rent BufferSize
    let outAvailable = resampler.ResampleOut(outBuffer, 0, inAvailable, BufferSize, 1)
    let buffer = Array.zeroCreate<float32> outAvailable
    Array.Copy(outBuffer, 0, buffer, 0, outAvailable)
    resamplePool.Return outBuffer
    buffer

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
type Resampler<'State> =
    private { channel: Channel<ResamplerInput<'State>> }
    interface IDisposable with
        member this.Dispose() = dispose this.channel

let Resampler<'State> samplesPerWindow (onResampled: ResamplerOutput<'State> -> unit) =
    let windowDuration = float samplesPerWindow / float SampleRate
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    let channel = Channel()
    let originalSamples = new List<float32>()
    let resampledSamples = new List<float32>()
    let rec consumer () =
        uply {
            let! action = readChannel' channel
            match action with
                | ResamplerInput.Reset ->
                    originalSamples.Clear()
                    resampledSamples.Clear()
                    onResampled Reset
                | ResamplerInput struct (state, frame) ->
                    originalSamples.AddRange frame.samples
                    resampledSamples.AddRange <|
                        if frame.format.SampleRate <> SampleRate then
                            resampler.SetRates(frame.format.SampleRate, SampleRate)
                            resample resampler frame.samples
                        else
                            frame.samples
                    if resampledSamples.Count >= samplesPerWindow then
                        let original =
                            let sampleSize = int <| windowDuration * float frame.format.SampleRate
                            let samples = originalSamples.GetRange(0, sampleSize)
                            originalSamples.RemoveRange(0, sampleSize)
                            samples.ToArray()
                        let resampled =
                            let samples = resampledSamples.GetRange(0, samplesPerWindow)
                            resampledSamples.RemoveRange(0, samplesPerWindow)
                            samples.ToArray()
                        let resampledAudio =
                            {   original =
                                    {   samples = original
                                        format = frame.format
                                    }
                                resampled =
                                    {   samples = resampled
                                        format = WriterFormat
                                    }
                            }
                        onResampled << ResamplerOutput <| struct (state, resampledAudio)
                    do! consumer()
            do! consumer()
        }
    fork' consumer
    { channel = channel }

/// Add audio samples to be processed by the resampler.
let writeResampler resampler = writeChannel resampler.channel