module Mirage.Core.Audio.Microphone.Resampler

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open NAudio.Dsp
open NAudio.Wave
open Mirage.Core.Audio.PCM

let [<Literal>] private SampleRate = 16000
let [<Literal>] private SamplesPerWindow = 1024
let private WindowDuration = float SamplesPerWindow / float SampleRate
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

type AudioFrame =
    {   samples: Samples
        format: WaveFormat
    }

type ResampledAudio =
    {   original: AudioFrame
        resampled: AudioFrame
    }

/// A live resampler for a microphone's input.
type Resampler =
    private { agent: BlockingQueueAgent<AudioFrame> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let Resampler (onResampled: ResampledAudio -> Async<Unit>) =
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    let agent = new BlockingQueueAgent<AudioFrame>(Int32.MaxValue)    
    let originalSamples = new List<float32>()
    let resampledSamples = new List<float32>()
    let rec consumer =
        async {
            let! frame = agent.AsyncGet()
            originalSamples.AddRange frame.samples
            resampledSamples.AddRange <|
                if frame.format.SampleRate <> SampleRate then
                    resampler.SetRates(frame.format.SampleRate, SampleRate)
                    resample resampler frame.samples
                else
                    frame.samples
            if resampledSamples.Count >= SamplesPerWindow then
                let original =
                    let sampleSize = int <| WindowDuration * float frame.format.SampleRate
                    let samples = originalSamples.GetRange(0, sampleSize)
                    originalSamples.RemoveRange(0, sampleSize)
                    samples.ToArray()
                let resampled =
                    let samples = resampledSamples.GetRange(0, SamplesPerWindow)
                    resampledSamples.RemoveRange(0, SamplesPerWindow)
                    samples.ToArray()
                do! onResampled <|
                    {   original =
                            {   samples = original
                                format = frame.format
                            }
                        resampled =
                            {   samples = resampled
                                format = WriterFormat
                            }
                    }
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

/// Add audio samples to be processed by the resampler.
let writeResampler = _.agent.AsyncAdd