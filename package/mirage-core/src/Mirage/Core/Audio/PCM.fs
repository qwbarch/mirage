module Mirage.Core.Audio.PCM

open System
open System.Buffers
open Mirage.Prelude

[<Struct>]
type WaveFormat =
    {   sampleRate: int
        channels: int
    }

/// Calculates the length of the given audio samples in milliseconds.
let inline audioLengthMs waveFormat sampleCount =
    int <| float sampleCount / float waveFormat.sampleRate / float waveFormat.channels * 1000.0

/// 16-bit audio represented as a byte[].  
/// This must be returned via __ArrayPool.Shared.Return__ when finished using it.
[<Struct>]
type PcmData =
    {   data: byte[]
        length: int
    }

/// 16-bit audio represented as a float32[].  
/// This must be returned via __ArrayPool.Shared.Return__ when finished using it.
[<Struct>]
type Samples =
    {   data: float32[]
        length: int
    }

/// Converts pcm data represented as a float32 array to a byte array, assuming it contains 2 bytes per sample.
/// Credits: https://stackoverflow.com/a/42151979
let toPcmData (samples: Samples) : PcmData =
    let bufferLength = samples.length * 2
    let buffer = ArrayPool.Shared.Rent bufferLength
    Array.Clear(buffer, 0, bufferLength)
    let mutable sampleIndex = 0
    let mutable pcmIndex = 0
    while sampleIndex < samples.length do
        let sample = int16 <| samples.data[sampleIndex] * float32 Int16.MaxValue
        buffer[pcmIndex] <- byte sample &&& 0xFFuy
        buffer[pcmIndex + 1] <- (byte sample >>> 8) &&& 0xFFuy
        &sampleIndex += 1
        &pcmIndex += 2
    { data = buffer; length = bufferLength }

/// Converts pcm data represented as a byte array to a float32 array, assuming it contains 2 bytes per sample.
/// Credits: https://www.codeproject.com/Articles/501521/How-to-convert-between-most-audio-formats-in-NET
let fromPcmData (pcmData: PcmData) : Samples =
    let bufferLength = pcmData.length / 2
    let buffer = ArrayPool.Shared.Rent bufferLength
    Array.Clear(buffer, 0, bufferLength)
    let mutable sampleIndex = 0
    for i in 0 .. bufferLength - 1 do
        let sample = BitConverter.ToInt16(pcmData.data, i * 2)
        buffer[sampleIndex] <- float32 sample / float32 Int16.MaxValue
        &sampleIndex += 1
    { data = buffer; length = bufferLength }