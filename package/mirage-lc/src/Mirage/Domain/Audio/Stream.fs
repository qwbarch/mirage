module Mirage.Domain.Audio.Stream

open System.Diagnostics
open System.Collections.Generic
open Mirage.Core.Audio.File.WaveReader
open Mirage.Prelude
open Mirage.Domain.Audio.Frame

let private maximumBuffer = float Stopwatch.Frequency * 2.0 // 2 seconds (of audio duration) buffered.
let private frequency = float Stopwatch.Frequency / 1000.0

/// <summary>
/// Streams audio using the given <b>sendFrame</b> function, internally handling the timing between payloads.
/// Note: This function implicitly disposes the <b>AudioReader</b> when it finishes processing frames.
/// </summary>
/// <param name="audioReader">
/// Source audio to read from. Since <b>streamAudio</b> disposes the audio reader when finished, you should not be using any of its methods after the stream starts.
/// </param>
/// <param name="sendFrame">
/// Function to run whenever frame data is available. A value of <b>None</b> is passed when the stream is over.
/// </param>
let streamAudio (waveReader: WaveReader) (sendFrame: Option<FrameData> -> Async<Unit>) : Async<Unit> =
    async {
        let mutable previousTime = 0.0
        let mutable currentBuffer = 0.0
        let mutable frame = waveReader.mp3Reader.ReadNextFrame()
        let delayBuffer = new LinkedList<float>()
        while not <| isNull frame do
            let currentTime = waveReader.mp3Reader.CurrentTime.TotalMilliseconds * frequency
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

            do! sendFrame << Some <|
                {   rawData = frame.RawData
                    sampleIndex = int waveReader.mp3Reader.tableOfContents[waveReader.mp3Reader.tocIndex - 1].SamplePosition
                }

            frame <- waveReader.mp3Reader.ReadNextFrame()
        do! sendFrame None
    }