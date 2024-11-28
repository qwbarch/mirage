module Mirage.Core.Audio.File.OpusReader

open Concentus.Oggfile
open System.IO
open Mirage.Core.Async.Fork

type OpusReader = { reader: OpusOggReadStream }

/// Reads an opus file from a background thread, and then returns it to the caller.
let readOpusFile filePath =
    forkReturn <| async {
        let! bytes = Async.AwaitTask <| File.ReadAllBytesAsync filePath
        let stream = new MemoryStream(bytes)
        let reader = OpusOggReadStream(null, stream)
        return { reader = reader }
    }