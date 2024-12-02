module Mirage.Domain.Audio.Stream

open System
open System.Diagnostics
open System.Collections.Generic
open Mirage.Prelude
open Mirage.Core.Audio.Wave.Reader
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Logger

let [<Literal>] private FrameSizeMs = 20

let private maximumBuffer = float Stopwatch.Frequency * 1.0 // 1 seconds (of audio duration) buffered.
let private frequency = float Stopwatch.Frequency / 1000.0

/// <summary>
/// Streams audio using the given <b>sendPacket</b> function, internally handling the timing between payloads.
/// Note: This function implicitly disposes the <b>OpusReader</b> when it finishes processing frames.
/// </summary>
/// <param name="waveReader">
/// Source audio to read from. Since <b>streamAudio</b> disposes the wave reader when finished, you should not be using any of its methods after the stream starts.
/// </param>
/// <param name="sendPacket">
/// Function to run whenever a packet is available. A value of <b>None</b> is passed when the stream is over.
/// </param>
let streamAudio waveReader (sendPacket: ValueOption<WavePacket> -> Async<Unit>) : Async<Unit> =
    async {
        let mutable sampleIndex = 0
        let mutable previousTime = 0.0
        let mutable currentBuffer = 0.0
        let delayBuffer = new LinkedList<float>()
        let format = waveReader.reader.WaveFormat
        let bytesPerSample = format.BitsPerSample / 8
        let frameSamples = FrameSizeMs * format.SampleRate / 1000
        let frameSize = frameSamples * format.Channels * bytesPerSample
        while sampleIndex < int waveReader.reader.SampleCount do
            let pcmData =
                let buffer = Array.zeroCreate<byte> frameSize
                let bytesRead = waveReader.reader.Read(buffer, 0, frameSize)
                if buffer.Length = bytesRead then
                    buffer
                else
                    let bytes = Array.zeroCreate<byte> bytesRead
                    Buffer.BlockCopy(buffer, 0, bytes, 0, bytesRead)
                    bytes

            let currentTime = waveReader.reader.CurrentTime.TotalMilliseconds * frequency
            let delay = currentTime - previousTime
            ignore <| delayBuffer.AddLast delay
            previousTime <- currentTime
            &currentBuffer += delay
            if currentBuffer >= maximumBuffer then
                let bufferedTime = delayBuffer.First.Value
                delayBuffer.RemoveFirst()
                let startTime = Stopwatch.GetTimestamp()
                let mutable delayedTime = 0.0
                while delayedTime < bufferedTime do
                    do! Async.Sleep 100
                    delayedTime <- float <| Stopwatch.GetTimestamp() - startTime
                // Async.sleep isn't accurate and can sleep less/more than what we wanted.
                // If it slept more than expected, we'll need to reduce the buffer a bit.
                let multiplier = if delayedTime > bufferedTime then 2.0 else 1.0
                &currentBuffer -= (delayedTime - bufferedTime * multiplier)

            do! sendPacket << ValueSome <|
                {   pcmData = pcmData
                    sampleIndex = sampleIndex
                }
            &sampleIndex += frameSamples

        do! sendPacket ValueNone
    }