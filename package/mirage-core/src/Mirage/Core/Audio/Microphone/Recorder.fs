module Mirage.Core.Audio.Microphone.Recorder

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Core.Audio.File.WaveWriter
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Resampler

type RecordStart =
    {   originalFormat: WaveFormat
        resampledFormat: WaveFormat
    }

type RecordFound =
    {   vadFrame: VADFrame
        fullAudio: ResampledAudio
        currentAudio: ResampledAudio
    }

/// Note: After the callback finishes for this action, the mp3 writer is disposed.
type RecordEnd =
    {   mp3Writer: WaveWriter
        vadFrame: VADFrame
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

let Recorder minAudioDurationMs directory (onRecording: RecordAction -> Async<Unit>) =
    let agent = new BlockingQueueAgent<DetectAction>(Int32.MaxValue)
    let rec consumer =
        async {
            let! action = agent.AsyncGet() 
            match action with
                | DetectStart payload ->
                    do! onRecording << RecordStart <|
                        {   originalFormat = payload.originalFormat
                            resampledFormat = payload.resampledFormat
                        }
                | DetectEnd payload ->
                    if payload.audioDurationMs > minAudioDurationMs then
                        let! waveWriter = createWaveWriter directory payload.fullAudio.original.format
                        do! writeWaveFile waveWriter payload.fullAudio.original.samples
                        do! onRecording << RecordEnd <|
                            {   mp3Writer = waveWriter
                                vadFrame = payload.vadFrame
                                vadTimings = payload.vadTimings
                                fullAudio = payload.fullAudio
                                currentAudio = payload.currentAudio
                                audioDurationMs = payload.audioDurationMs
                            }
                        do! closeMp3Writer waveWriter
                | DetectFound payload ->
                    do! onRecording << RecordFound <|
                        {   vadFrame = payload.vadFrame
                            fullAudio = payload.fullAudio
                            currentAudio = payload.currentAudio
                        }
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

let writeRecorder = _.agent.AsyncAdd