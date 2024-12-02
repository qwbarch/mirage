module Mirage.Core.Audio.Microphone.Resampler

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open Mirage.Core.Audio.PCM
open NAudio.Wave
open NAudio.Dsp

let [<Literal>] private SampleRate = 16000
let private WriterFormat = WaveFormat(SampleRate, 1)

/// Resamples the given audio samples using the resampler's configured in/out sample rates.<br />
/// This assumes the input/output is mono-channel audio.<br />
/// Source: https://markheath.net/post/fully-managed-input-driven-resampling-wdl
let private resample (resampler: WdlResampler) (samples: Samples) : Samples =
    let mutable inBuffer = null
    let mutable inBufferOffset = 0
    let inAvailable = resampler.ResamplePrepare(samples.Length, 1, &inBuffer, &inBufferOffset)
    Array.Copy(samples, 0, inBuffer, inBufferOffset, inAvailable)
    let mutable outBuffer = Array.zeroCreate<float32> 2000
    let outAvailable = resampler.ResampleOut(outBuffer, 0, inAvailable, outBuffer.Length, 1)
    let buffer = Array.zeroCreate<float32> outAvailable
    Array.Copy(outBuffer, 0, buffer, 0, outAvailable)
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
    private { agent: BlockingQueueAgent<ResamplerInput<'State>> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let Resampler<'State> samplesPerWindow (onResampled: ResamplerOutput<'State> -> Async<Unit>) =
    let windowDuration = float samplesPerWindow / float SampleRate
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    let agent = new BlockingQueueAgent<ResamplerInput<'State>>(Int32.MaxValue)    
    let originalSamples = new List<float32>()
    let resampledSamples = new List<float32>()
    let rec consumer =
        async {
            do! agent.AsyncGet() >>= function
                | ResamplerInput.Reset ->
                    async {
                        originalSamples.Clear()
                        resampledSamples.Clear()
                        do! onResampled Reset
                    }
                | ResamplerInput struct (state, frame) ->
                    async {
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
                            do! onResampled << ResamplerOutput <| struct (state, resampledAudio)
                        do! consumer
                    }
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

/// Add audio samples to be processed by the resampler.
let writeResampler = _.agent.AsyncAdd