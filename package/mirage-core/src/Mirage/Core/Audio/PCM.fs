module Mirage.Core.Audio.PCM

open System

let [<Literal>] BytesPerSample = 2

/// Converts pcm data represented as a byte array to a float32 array, assuming it contains 2 bytes per sample.
let fromPCMBytes (pcmData: byte[]) : float32[] =
    let sampleCount = pcmData.Length / BytesPerSample
    Array.init sampleCount <| fun i ->
        let sampleValue = BitConverter.ToInt16(pcmData, i * BytesPerSample)
        float32 sampleValue / 32768.0f

/// Converts pcm data represented as a float32 array to a byte array, assuming it contains 2 bytes per sample.
let toPCMBytes (floatData: float32[]) : byte[] =
    Array.init (floatData.Length * BytesPerSample) <| fun i ->
        let bytes = BitConverter.GetBytes(int16 <| floatData.[i / BytesPerSample] * 32768.0f)
        bytes.[i % BytesPerSample]