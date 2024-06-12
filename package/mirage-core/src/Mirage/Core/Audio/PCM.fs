module Mirage.Core.Audio.PCM

open System
open NAudio.Wave

let [<Literal>] BytesPerSample = 2

type PCMData = byte[]
type Samples = float32[]

/// Converts pcm data represented as a byte array to a float32 array, assuming it contains 2 bytes per sample.
let fromPCMBytes (pcmData: PCMData) : Samples =
    let sampleCount = pcmData.Length / BytesPerSample
    Array.init sampleCount <| fun i ->
        let sampleValue = BitConverter.ToInt16(pcmData, i * BytesPerSample)
        float32 sampleValue / 32768.0f

/// Converts pcm data represented as a float32 array to a byte array, assuming it contains 2 bytes per sample.
let toPCMBytes (floatData: Samples) : PCMData =
    let buffer = Array.zeroCreate<byte> sizeof<int16>
    Array.init (floatData.Length * BytesPerSample) <| fun i ->
        let value = int16 <| floatData[i / BytesPerSample] * 32768.0f
        buffer[0] <- byte (value &&& 0xFFs)
        buffer[1] <- byte (value >>> 8 &&& 0xFFs)
        buffer[i % BytesPerSample]

/// Calculates the length of the given audio samples in milliseconds.
let audioLengthMs (waveFormat: WaveFormat) (samples: Samples) =
    int <| float samples.Length / float waveFormat.SampleRate / float waveFormat.Channels * 1000.0