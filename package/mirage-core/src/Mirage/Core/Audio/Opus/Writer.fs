module Mirage.Core.Audio.Opus.Writer

#nowarn "40"

open System
open System.IO
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open Concentus.Oggfile
open NAudio.Wave
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Codec

type private WriteAction
    = WriteSamples of Samples
    | Close

type OpusWriter = private { channel: BlockingQueueAgent<WriteAction> }

type OpusWriterArgs =
    {   filePath: string
        format: WaveFormat
    }

let OpusWriter args =
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
                            dispose channel
                        }
                do! consumer
        }
    Async.Start consumer
    {   channel = channel
    }

/// Write float samples into the opus writer, which will be automatically resampled and encoded.
let writeOpusSamples opusWriter = opusWriter.channel.AsyncAdd << WriteSamples

/// Close the opus writer and flush it to disk.
let closeOpusWriter opusWriter = opusWriter.channel.AsyncAdd Close