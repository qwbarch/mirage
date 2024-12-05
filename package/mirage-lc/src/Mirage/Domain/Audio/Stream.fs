module Mirage.Domain.Audio.Stream

open IcedTasks
open System
open System.Buffers
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
        let delayBuffer = new LinkedList<float>()
        printfn "streamAudio Start"
        try
            while hasNextPacket opusReader do
                let rawPacket = readNextRawPacket opusReader
                let currentTime = (getCurrentTime opusReader).TotalMilliseconds * frequency
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

                // Since readNextRawPacket internally returns the previous packet's array
                // to the ArrayPool, we must copy it to avoid referencing a returned array.
                let opusData = ArrayPool.Shared.Rent rawPacket.packetLength
                ignore <| Buffer.BlockCopy(rawPacket.packet, 0, opusData, 0, rawPacket.packetLength)

                printfn "streamAudio sending packet"
                do! sendPacket << ValueSome <|
                    {   opusData = opusData
                        opusDataLength = rawPacket.packetLength
                        sampleIndex = sampleIndex
                    }
                &sampleIndex += SamplesPerPacket
        with | ex -> printfn $"error while streaming audio: {ex}"
        printfn "streamAudio End"
        do! sendPacket ValueNone
    }