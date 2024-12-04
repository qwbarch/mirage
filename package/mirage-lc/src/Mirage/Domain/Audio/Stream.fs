module Mirage.Domain.Audio.Stream

open IcedTasks
open System.Diagnostics
open System.Threading.Tasks
open System.Collections.Generic
open Mirage.Prelude
open Mirage.Core.Audio.Opus.Reader
open Mirage.Domain.Audio.Packet
open Mirage.Core.Audio.Opus.Codec

let [<Literal>] MinimumBufferedAudioMs = 1000

let private maximumBuffer = float Stopwatch.Frequency * float MinimumBufferedAudioMs / 1000.0
let private frequency = float Stopwatch.Frequency / 1000.0

/// <summary>
/// Streams audio using the given <b>sendPacket</b> function, internally handling the timing between payloads.
/// Note: This function implicitly disposes the <b>OpusReader</b> when it finishes processing frames.
/// </summary>
/// <param name="opusReader">
/// Source audio to read from. Since <b>streamAudio</b> disposes the opus reader when finished, you should not be using any of its methods after the stream starts.
/// </param>
/// <param name="sendPacket">
/// Function to run whenever a packet is available. A value of <b>None</b> is passed when the stream is over.
/// </param>
let streamAudio opusReader cancellationToken (sendPacket: voption<OpusPacket> -> ValueTask<unit>) =
    valueTask {
        let mutable sampleIndex = 0
        let mutable previousTime = 0.0
        let mutable currentBuffer = 0.0
        let mutable packet = opusReader.reader.ReadNextRawPacket()
        let delayBuffer = new LinkedList<float>()
        while not <| isNull packet do
            let currentTime = opusReader.reader.CurrentTime.TotalMilliseconds * frequency
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
                    do! Task.Delay(100, cancellationToken)
                    delayedTime <- float <| Stopwatch.GetTimestamp() - startTime
                // Task.Delay isn't accurate and can sleep less/more than what we wanted.
                // If it slept more than expected, we'll need to reduce the buffer a bit.
                let multiplier = if delayedTime > bufferedTime then 2.0 else 1.0
                &currentBuffer -= (delayedTime - bufferedTime * multiplier)
            do! sendPacket << ValueSome <|
                {   opusData = packet
                    sampleIndex = sampleIndex
                }
            &sampleIndex += SamplesPerPacket
            packet <- opusReader.reader.ReadNextRawPacket()
        do! sendPacket ValueNone
    }