module Mirage.Core.Audio.File.Mp3Reader

open System
open System.IO
open FSharpPlus
open NAudio.Wave

type Mp3Reader =
    { reader: Mp3FileReader }
    interface IDisposable with
        member this.Dispose() =
            dispose this.reader.mp3Stream
            dispose this.reader

let Mp3Reader filePath =
    async {
        let! bytes = Async.AwaitTask <| File.ReadAllBytesAsync filePath
        return { reader = new Mp3FileReader(new MemoryStream(bytes)) }
    }