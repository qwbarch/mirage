module Mirage.Core.Audio.File.Mp3Writer

#nowarn "40"

open System
open System.IO
open FSharpPlus
open FSharpx.Control
open NAudio.Lame
open NAudio.Wave
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.Fork

type private Mp3Action
    = WriteSamples of ValueTuple<float32[], float32[]>
    | Dispose

type Mp3Writer =
    private
        {   channel: BlockingQueueAgent<Mp3Action>
            writer: LameMP3FileWriter
            fileId: Guid
            filePath: string
        }
    interface IDisposable with
        member this.Dispose() = this.channel.Add Dispose

let createMp3Writer (directory: string) inputFormat resampledInputFormat (preset: LAMEPreset) =
    async {
        // Create the directory on a background thread.
        do! forkReturn <| async {
            ignore <| Directory.CreateDirectory directory
        }
        let fileId = Guid.NewGuid()
        let filePath = Path.Join(directory, $"{fileId}.mp3")
        let writer = new LameMP3FileWriter(filePath, inputFormat, preset)
        let channel = new BlockingQueueAgent<Mp3Action>(Int32.MaxValue)
        let mutable disposed = false

        let waveWriter = new WaveFileWriter(Path.Join(directory, $"{fileId}.wav"), inputFormat)
        //let resampledWriter = new WaveFileWriter(Path.Join(directory, $"{fileId}.resampled.wav"), resampledInputFormat)

        //https://github.com/lostromb/concentus/blob/master/CSharp/ConcentusDemo/ConcentusCodec.cs

        let rec consumer =
            async {
                let! action = channel.AsyncGet()
                match action with
                    | WriteSamples (samples, resampledSamples) ->
                        if not disposed then
                            do! writer.AsyncWrite <| toPCMBytes samples

                            waveWriter.WriteSamples(samples, 0, samples.Length)
                            //resampledWriter.WriteSamples(resampledSamples, 0, resampledSamples.Length)

                            do! consumer
                    | Dispose ->
                        if not disposed then
                            disposed <- true
                            do! Async.AwaitTask(writer.FlushAsync())

                            waveWriter.Flush()
                            dispose waveWriter

                            //resampledWriter.Flush()
                            //dispose resampledWriter

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