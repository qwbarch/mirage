module Mirage.Core.Audio.File.Mp3Writer

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open NAudio.Lame
open Mirage.Core.Audio.PCM
open System.IO
open Mirage.Core.Async.Fork

type private Mp3Action
    = WriteSamples of float32[]
    | Dispose

type Mp3Writer =
    private
        {   channel: BlockingQueueAgent<Mp3Action>
            writer: LameMP3FileWriter
            fileId: Guid
            filePath: string
        }

let createMp3Writer (directory: string) inputFormat (preset: LAMEPreset) =
    async {
        // Create the directory on a background thread.
        do! forkReturn <| async {
            ignore << Directory.CreateDirectory <| Path.GetDirectoryName directory
        }
        let fileId = Guid.NewGuid()
        let filePath = Path.Join(directory, $"{fileId}.mp3")
        let writer = new LameMP3FileWriter(filePath, inputFormat, preset)
        let channel = new BlockingQueueAgent<Mp3Action>(Int32.MaxValue)
        let rec consumer =
            async {
                let! action = channel.AsyncGet()
                match action with
                    | WriteSamples samples ->
                        do! toPCMBytes samples
                            |> writer.WriteAsync
                            |> _.AsTask()
                            |> Async.AwaitTask
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
let writeMp3File mp3Writer = mp3Writer.channel.AsyncAdd << WriteSamples

/// Closes the mp3 file.
let closeMp3Writer mp3Writer = mp3Writer.channel.AsyncAdd Dispose

/// Retrieve the identifier of the file.
let getFileId mp3Writer = mp3Writer.fileId

/// Get the file path of the mp3 file.
let getFilePath mp3Writer = mp3Writer.filePath