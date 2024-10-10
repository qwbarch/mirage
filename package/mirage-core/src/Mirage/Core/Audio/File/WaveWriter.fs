module Mirage.Core.Audio.File.WaveWriter

#nowarn "40"

open System
open System.IO
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Core.Async.Fork

type private WaveAction
    = WriteSamples of float32[]
    | Dispose

type WaveWriter =
    private
        {   channel: BlockingQueueAgent<WaveAction>
            writer: WaveFileWriter
            fileId: Guid
            filePath: string
        }

let createWaveWriter (directory: string) inputFormat =
    async {
        // Create the directory on a background thread.
        do! forkReturn <| async {
            ignore <| Directory.CreateDirectory directory
        }
        let fileId = Guid.NewGuid()
        let filePath = Path.Join(directory, $"{fileId}.wav")
        let writer = new WaveFileWriter(filePath, inputFormat)
        let channel = new BlockingQueueAgent<WaveAction>(Int32.MaxValue)
        let rec consumer =
            async {
                let! action = channel.AsyncGet()
                match action with
                    | WriteSamples samples ->
                        writer.WriteSamples(samples, 0, samples.Length)
                        do! consumer
                    | Dispose ->
                        do! Async.AwaitTask(writer.FlushAsync())
                        dispose writer
                        dispose channel
            }
        Async.Start consumer
        return {
            channel = channel
            writer = writer
            fileId = fileId
            filePath = filePath
        }
    }

/// Write the given frame of pcm data into the mp3 file.
let writeWaveFile waveWriter = waveWriter.channel.AsyncAdd << WriteSamples

/// Closes the mp3 file.
let closeMp3Writer waveWriter = waveWriter.channel.AsyncAdd Dispose

/// Retrieve the identifier of the file.
let getFileId = _.fileId

/// Get the file path of the mp3 file.
let getFilePath mp3Writer = mp3Writer.filePath