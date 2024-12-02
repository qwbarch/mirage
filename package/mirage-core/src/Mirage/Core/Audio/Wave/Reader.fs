module Mirage.Core.Audio.Wave.Reader

open System
open System.IO
open FSharpPlus
open NAudio.Wave
open Mirage.Core.Async.Fork

type WaveReader =
    { reader: WaveFileReader }
    interface IDisposable with
        member this.Dispose() = dispose this.reader

let readWavFile filePath =
    forkReturn <| async {
        let! bytes = Async.AwaitTask <| File.ReadAllBytesAsync filePath
        let memoryStream = new MemoryStream(bytes)
        return { reader = new WaveFileReader(memoryStream) }
    }