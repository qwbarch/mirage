module Mirage.Core.Audio.Microphone.Recognition

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Detection

type TranscribeInput =
    {   samplesBatch: float32[][]
        language: string
    }

type Transcription =
    {   text: string
        avgLogProb: int
        noSpeechProb: int
    }

/// This action only runs when samples are available (array length is > 0).
type TranscribeFound =
    {   mp3Writer: Mp3Writer
        vadFrame: VADFrame
        audio: ResampledAudio
        transcriptions: Transcription[]
    }

type TranscribeEnd =
    {   mp3Writer: Mp3Writer
        vadTimings: list<VADFrame>
        audioDurationMs: int
    }

/// A sum type representing stages of a live transcription.
type TranscribeAction
    = TranscribeStart
    | TranscribeFound of TranscribeFound
    | TranscribeEnd of TranscribeEnd

/// A function that executes whenever a transcription finishes.
type OnTranscribe = TranscribeAction -> Async<Unit>

// Transcribe voice audio into text.
type VoiceTranscriber =
    private { agent: BlockingQueueAgent<RecordAction> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let VoiceTranscriber (transcribe: TranscribeInput -> Async<Transcription[]>) (onTranscribe: OnTranscribe) =
    let agent = new BlockingQueueAgent<RecordAction>(Int32.MaxValue)
    let rec consumer =
        async {
            let! action = agent.AsyncGet()
            match action with
                | RecordStart _ ->
                    do! onTranscribe TranscribeStart
                | RecordEnd payload ->
                    do! onTranscribe << TranscribeEnd <|
                        {   mp3Writer = payload.mp3Writer
                            vadTimings = payload.vadTimings
                            audioDurationMs = payload.audioDurationMs
                        }
                | RecordFound payload ->
                    if payload.audio.resampled.samples.Length > 0 then
                        let! transcriptions =
                            transcribe
                                {   samplesBatch = [|payload.audio.resampled.samples|]
                                    language = "en"
                                }
                        do! onTranscribe << TranscribeFound <|
                            {   mp3Writer = payload.mp3Writer
                                vadFrame = payload.vadFrame
                                audio = payload.audio
                                transcriptions = transcriptions
                            }
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

let writeTranscriber = _.agent.AsyncAdd