module Mirage.Core.Audio.Resampler

open System
open NAudio.Dsp

type Resampler = private Resampler of WdlResampler

let Resampler () =
    let resampler = WdlResampler()
    resampler.SetMode(true, 2, false)
    resampler.SetFilterParms()
    resampler.SetFeedMode true
    Resampler resampler

/// Set the input/output sample rate.
let setRates (Resampler resampler) input output = resampler.SetRates(input, output)

/// Resamples the given audio samples using the resampler's configured in/out sample rates.<br />
/// This assumes the input/output is mono-channel audio.<br />
/// Source: https://markheath.net/post/fully-managed-input-driven-resampling-wdl
let resample (Resampler resampler) (samples: float32[]) =
    let mutable inBuffer = null
    let mutable inBufferOffset = 0
    let inAvailable = resampler.ResamplePrepare(samples.Length, 1, &inBuffer, &inBufferOffset)
    Array.Copy(samples, 0, inBuffer, inBufferOffset, inAvailable)
    let mutable outBuffer = Array.zeroCreate<float32> 2000
    let outAvailable = resampler.ResampleOut(outBuffer, 0, inAvailable, outBuffer.Length, 1)
    let buffer = Array.zeroCreate<float32> outAvailable
    Array.Copy(outBuffer, 0, buffer, 0, outAvailable)
    buffer