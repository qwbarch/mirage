module Mirage.Core.Audio.Wave.Writer

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Core.Audio.PCM
open System.IO

type private WriteAction
    = WriteSamples of Samples
    | Close

type WaveWriter = private { channel: BlockingQueueAgent<WriteAction> }

let WaveWriter filePath format =
    let fullSamples = List<float32>()
    let channel = new BlockingQueueAgent<WriteAction>(Int32.MaxValue)
    let mutable closed = false
    let rec consumer =
        async {
            if not closed then
                do! channel.AsyncGet() >>= function
                    | WriteSamples samples ->
                        result <| fullSamples.AddRange samples
                    | Close ->
                        async {
                            closed <- true
                            use fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write)
                            use writer = new WaveFileWriter(fileStream, format)
                            writer.WriteSamples(fullSamples.ToArray(), 0, fullSamples.Count)
                            do! Async.AwaitTask(writer.FlushAsync())
                        }
                do! consumer
        }
    Async.Start consumer
    { channel = channel }

// Write float samples to be flushed to disk, when calling __closeWaveWriter__.
let writeWaveSamples waveWriter = waveWriter.channel.AsyncAdd << WriteSamples

// Flush all written samples to disk.
let closeWaveWriter waveWriter = waveWriter.channel.AsyncAdd Close