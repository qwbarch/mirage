module Mirage.Core.Audio.Opus.Reader

open FSharpPlus
open System
open System.IO
open System.Threading
open System.Buffers
open Concentus.Oggfile
open IcedTasks
open Mirage.Prelude
open Mirage.Core.Audio.Opus.Codec
open Mirage.Core.Task.Fork

type OpusReader =
    {   reader: OpusOggRentReadStream
        fileStream: FileStream
        totalSamples: int
    }
    interface IDisposable with
        member this.Dispose() =
            dispose this.fileStream

/// Reads an opus file from a background thread, and then returns it to the caller.
let readOpusFile filePath =
    forkReturn CancellationToken.None <| fun () -> valueTask {
        let createFileStream () = new FileStream(filePath, FileMode.Open, FileAccess.Read)
        let totalSamples =
            use fileStream = createFileStream()
            let reader = OpusOggRentReadStream fileStream
            let mutable packets = 0
            while reader.HasNextPacket do
                let rentedPacket = reader.RentNextRawPacket()
                if not <| isNull rentedPacket.packet then
                    ArrayPool.Shared.Return rentedPacket.packet
                    &packets += 1
            packets * SamplesPerPacket
        let fileStream = createFileStream()
        return  {
            reader = OpusOggRentReadStream fileStream
            fileStream = fileStream
            totalSamples = totalSamples
        }
    }