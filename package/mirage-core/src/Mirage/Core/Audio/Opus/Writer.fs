module Mirage.Core.Audio.Opus.Writer

#nowarn "40"

open Collections.Pooled
open Concentus.Oggfile
open FSharpPlus
open System
open System.IO
open System.Buffers
open Mirage.Core.Pooled
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Opus.Codec

type OpusWriter =
    private
        {   samples: PooledList<float32>
            filePath: string
            format: WaveFormat
            mutable closed: bool
            mutable disposed: bool
        }
    interface IDisposable with
        member this.Dispose() =
            if not this.disposed then
                this.disposed <- true
                dispose this.samples

[<Struct>]
type OpusWriterArgs =
    {   filePath: string
        format: WaveFormat
    }

let OpusWriter args =
    {   samples = new PooledList<float32>(ClearMode.Never, ArrayPool.Shared)
        filePath = args.filePath
        format = args.format
        closed = false
        disposed = false
    }

/// Write float samples into memory.
let writeOpusSamples opusWriter samples = 
    appendSegment opusWriter.samples <| ArraySegment(samples.data, 0, samples.length)

/// Encode the audio into opus data, and then write it to file. The OpusWriter is implicitly disposed using this function.
/// __Note: This is done on the caller thread.__
let closeOpusWriter opusWriter =
    if not opusWriter.closed then
        opusWriter.closed <- true
        ignore << Directory.CreateDirectory <| Path.GetDirectoryName opusWriter.filePath
        use fileStream = new FileStream(opusWriter.filePath, FileMode.Create, FileAccess.Write)
        use encoder = OpusEncoder()
        let opusStream = OpusOggWriteStream(
            encoder,
            fileStream,
            null, // Opus tags.
            opusWriter.format.sampleRate
        )
        let samples = ArrayPool.Shared.Rent opusWriter.samples.Count
        copyFrom opusWriter.samples samples opusWriter.samples.Count
        try
            opusStream.WriteSamples(samples, 0, opusWriter.samples.Count)
            opusStream.Finish()
        finally
            ArrayPool.Shared.Return samples
            dispose opusWriter