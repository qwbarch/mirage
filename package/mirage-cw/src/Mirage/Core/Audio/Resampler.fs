module Mirage.Core.Audio.Resampler

open System
open NAudio.Dsp

type Resampler = Resampler of WdlResampler

let defaultResampler (inputSampleRate: int) (outputSampleRate: int) =
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    resampler.SetRates(inputSampleRate, outputSampleRate)
    Resampler resampler

/// Resamples the given audio samples using the resampler's configured in/out sample rates.
/// Source: https://markheath.net/post/fully-managed-input-driven-resampling-wdl
let resample (Resampler resampler) (samples: float32[]) =
    let mutable inBuffer = Unchecked.defaultof<float32[]>
    let mutable inBufferOffset = 0
    let inAvailable = resampler.ResamplePrepare(samples.Length, 1, &inBuffer, &inBufferOffset)
    Array.Copy(samples, 0, inBuffer, inBufferOffset, inAvailable)
    let mutable outBuffer = Array.zeroCreate<float32> 2000
    ignore <| resampler.ResampleOut(outBuffer, 0, inAvailable, outBuffer.Length, 1)
    outBuffer