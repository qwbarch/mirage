module Mirage.Core.Audio.File.Mp3Reader

open System
open System.IO
open FSharpPlus
open NAudio.Wave
open Mirage.Core.Async.Fork

type Mp3Reader =
    { reader: Mp3FileReader }
    interface IDisposable with
        member this.Dispose() =
            dispose this.reader.mp3Stream
            dispose this.reader

/// Loads the mp3 file from a background thread, and then passes it back to the caller thread.
let readMp3File (filePath: string) =
    forkReturn <| async {
        let! bytes = Async.AwaitTask <| File.ReadAllBytesAsync filePath
        return { reader = new Mp3FileReader(new MemoryStream(bytes)) }
    }