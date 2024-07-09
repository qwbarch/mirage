module Mirage.Core.Audio.Microphone.Recorder

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open NAudio.Lame
open NAudio.Wave
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Resampler

let [<Literal>] private WriterPreset = LAMEPreset.STANDARD

type RecordStart =
    {   mp3Writer: Mp3Writer
        originalFormat: WaveFormat
        resampledFormat: WaveFormat
    }

type RecordFound =
    {   mp3Writer: Mp3Writer
        vadFrame: VADFrame
        fullAudio: ResampledAudio
        currentAudio: ResampledAudio
    }

/// Note: After the callback finishes for this action, the mp3 writer is disposed.
type RecordEnd =
    {   mp3Writer: Mp3Writer
        vadTimings: list<VADFrame>
        fullAudio: ResampledAudio
        currentAudio: ResampledAudio
        audioDurationMs: int
    }

/// A sum type representing the progress of a recording.
type RecordAction
    = RecordStart of RecordStart
    | RecordFound of RecordFound
    | RecordEnd of RecordEnd

/// Records audio from a live microphone feed.
type Recorder =
    private { agent: BlockingQueueAgent<DetectAction> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let Recorder directory (onRecording: RecordAction -> Async<Unit>) =
    let agent = new BlockingQueueAgent<DetectAction>(Int32.MaxValue)
    let mutable mp3Writer = None
    let rec consumer =
        async {
            let! action = agent.AsyncGet() 
            match action with
                | DetectStart payload ->
                    let! writer = createMp3Writer directory payload.originalFormat WriterPreset
                    mp3Writer <- Some writer
                    do! onRecording << RecordStart <|
                        {   mp3Writer = mp3Writer.Value
                            originalFormat = payload.originalFormat
                            resampledFormat = payload.resampledFormat
                        }
                | DetectEnd payload ->
                    do! onRecording << RecordEnd <|
                        {   mp3Writer = mp3Writer.Value
                            vadTimings = payload.vadTimings
                            fullAudio = payload.fullAudio
                            currentAudio = payload.currentAudio
                            audioDurationMs = payload.audioDurationMs
                        }
                    do! closeMp3Writer mp3Writer.Value
                | DetectFound payload ->
                    do! writeMp3File mp3Writer.Value payload.currentAudio.original.samples
                    do! onRecording << RecordFound <|
                        {   mp3Writer = mp3Writer.Value
                            vadFrame = payload.vadFrame
                            fullAudio = payload.fullAudio
                            currentAudio = payload.currentAudio
                        }
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

let writeRecorder = _.agent.AsyncAdd