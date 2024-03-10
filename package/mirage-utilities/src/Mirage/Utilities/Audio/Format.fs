module Mirage.Utilities.Audio.PCM

open System

/// <summary>
/// Converts pcm data represented as a byte array to a float32 array, assuming it contains 2 bytes per sample.
/// </summary>
let toPcmBytes (pcmData: byte[]) : float32[] =
    let bytesPerSample = 2
    let sampleCount = pcmData.Length / bytesPerSample
    Array.init sampleCount <| fun i ->
        let sampleValue = BitConverter.ToInt16(pcmData, i * bytesPerSample)
        float32 sampleValue / 32768.0f