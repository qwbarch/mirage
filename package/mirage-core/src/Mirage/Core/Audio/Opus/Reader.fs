module Mirage.Core.Audio.Opus.Reader

open System.IO
open Concentus.Oggfile
open Mirage.Prelude
open Mirage.Core.Async.Fork
open Mirage.Core.Audio.Opus.Codec

type OpusReader =
    {   reader: OpusOggReadStream
        totalSamples: int
    }

/// Reads an opus file from a background thread, and then returns it to the caller.
let readOpusFile filePath =
    forkReturn <| async {
        let! bytes = Async.AwaitTask <| File.ReadAllBytesAsync filePath
        let memoryStream = new MemoryStream(bytes)
        let totalSamples =
            let reader = OpusOggReadStream(null, memoryStream)
            let mutable packets = 0
            while reader.HasNextPacket do
                let packet = reader.ReadNextRawPacket()
                if not <| isNull packet then
                    &packets += 1
            packets * SamplesPerPacket
        memoryStream.Position <- 0
        return  {
            reader = OpusOggReadStream(null, memoryStream)
            totalSamples = totalSamples
        }
    }