module Mirage.Core.Audio.Opus.Writer

#nowarn "40"

open Concentus.Oggfile
open System.IO
open System.Collections.Generic
open System.Threading
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
    let fullSamples = List<float32>()
    let channel = Channel CancellationToken.None
    let mutable closed = false
    let consumer () =
        forever <| fun () -> valueTask {
            if not closed then
                let! action = readChannel channel
                match action with
                    | WriteSamples samples ->
                        fullSamples.AddRange samples
                    | Close ->
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
                        opusStream.WriteSamples(fullSamples.ToArray(), 0, fullSamples.Count)
                        opusStream.Finish()
        }
    fork CancellationToken.None consumer
    { channel = channel }

/// Write float samples into the opus writer, which will be automatically resampled and encoded.
let writeOpusSamples opusWriter samples = writeChannel opusWriter.channel <| WriteSamples samples

/// Close the opus writer and flush it to disk.
let closeOpusWriter opusWriter = writeChannel opusWriter.channel Close