module Mirage.Core.Audio.Opus.Writer

#nowarn "40"

open Collections.Pooled
open Concentus.Oggfile
open FSharpPlus
open System
open System.IO
open System.Threading
open System.Buffers
open NAudio.Wave
open IcedTasks
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Codec
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Task.Utility

type private WriteAction
    = WriteSamples of Samples
    | Close

type OpusWriter = private { channel: Channel<WriteAction> }

[<Struct>]
type OpusWriterArgs =
    {   filePath: string
        format: WaveFormat
    }

let OpusWriter args =
    let fullSamples = new PooledList<float32>(ClearMode.Never, ArrayPool.Shared)
    let channel = Channel CancellationToken.None
    let mutable closed = false
    let consumer () =
        forever <| fun () -> valueTask {
            if not closed then
                let! action = readChannel channel
                match action with
                    | WriteSamples samples ->
                        appendSegment fullSamples <| ArraySegment(samples.data, 0, samples.length)
                    | Close ->
                        try
                            closed <- true
                            ignore << Directory.CreateDirectory <| Path.GetDirectoryName args.filePath
                            use fileStream = new FileStream(args.filePath, FileMode.Create, FileAccess.Write)
                            use encoder = OpusEncoder()
                            let opusStream = OpusOggWriteStream(
                                encoder,
                                fileStream,
                                null, // Opus tags.
                                args.format.sampleRate
                            )
                            let samples = ArrayPool.Shared.Rent fullSamples.Count
                            try
                                opusStream.WriteSamples(fullSamples.ToArray(), 0, fullSamples.Count)
                                opusStream.Finish()
                            finally
                                ArrayPool.Shared.Return samples
                        finally
                            dispose fullSamples
        }
    fork CancellationToken.None consumer
    { channel = channel }

/// Write float samples into the opus writer, which will be automatically resampled and encoded.
let writeOpusSamples opusWriter samples = writeChannel opusWriter.channel <| WriteSamples samples

/// Close the opus writer and flush it to disk.
let closeOpusWriter opusWriter = writeChannel opusWriter.channel Close