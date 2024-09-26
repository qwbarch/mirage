module Mirage.Core.Audio.File.WaveReader

open System
open System.IO
open FSharpPlus
open NAudio.Wave
open NAudio.Lame
open Mirage.Core.Async.Fork

type WaveReader =
    { mp3Reader: Mp3FileReader }
    interface IDisposable with
        member this.Dispose() =
            dispose this.mp3Reader.mp3Stream
            dispose this.mp3Reader

/// Loads the mp3 file from a background thread, and then passes it back to the caller thread.
let readWavFile (filePath: string) =
    forkReturn <| async {
        let! bytes = Async.AwaitTask <| File.ReadAllBytesAsync filePath
        use waveStream = new MemoryStream(bytes)
        use waveReader = new WaveFileReader(waveStream)
        let mp3Stream = new MemoryStream()
        use mp3Writer = new LameMP3FileWriter(mp3Stream, waveReader.WaveFormat, LAMEPreset.STANDARD)
        waveReader.CopyTo mp3Writer
        do! Async.AwaitTask(mp3Writer.FlushAsync())
        mp3Stream.Position <- 9
        return { mp3Reader = new Mp3FileReader(mp3Stream) }
    }