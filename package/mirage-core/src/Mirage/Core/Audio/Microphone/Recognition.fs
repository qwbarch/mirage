module Mirage.Core.Audio.Microphone.Recognition

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open Mirage.Core.Async.Lock
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Detection

type TranscriberInput
    = Transcribe of RecordAction
    | SetLanguage of string

type TranscribeRequest =
    {   samplesBatch: Samples[]
        language: string
    }

type Transcription =
    {   text: string
        avgLogProb: float32
        noSpeechProb: float32
    }

/// This action only runs when samples are available (array length is > 0).
type TranscribeFound<'Transcription> =
    {   mp3Writer: Mp3Writer
        vadFrame: VADFrame
        audio: ResampledAudio
        transcriptions: 'Transcription[]
    }

type TranscribeEnd<'Transcription> =
    {   mp3Writer: Mp3Writer
        vadTimings: list<VADFrame>
        audioDurationMs: int
        transcriptions: 'Transcription[]
    }

/// A sum type representing stages of a live transcription.
type TranscribeAction<'Transcription>
    = TranscribeStart
    | TranscribeFound of TranscribeFound<'Transcription>
    | TranscribeEnd of TranscribeEnd<'Transcription>

// Transcribe voice audio into text.
type VoiceTranscriber =
    private { agent: BlockingQueueAgent<TranscriberInput> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let VoiceTranscriber<'Transcription> (transcribe: TranscribeRequest -> Async<'Transcription[]>) (onTranscribe: TranscribeAction<'Transcription> -> Async<Unit>) =
    let agent = new BlockingQueueAgent<TranscriberInput>(Int32.MaxValue)
    let lock = createLock()
    let mutable language = "en"
    let samplesBuffer = new List<float32>()
    let rec consumer =
        async {
            let! input = agent.AsyncGet()
            match input with
                | SetLanguage lang -> language <- lang
                | Transcribe action ->
                    Async.StartImmediate <| async {
                        match action with
                            | RecordStart _ ->
                                samplesBuffer.Clear()
                                do! onTranscribe TranscribeStart
                            | RecordEnd payload ->
                                do! withLock' lock <| async {
                                    let! transcriptions =
                                        transcribe
                                            {   samplesBatch = [|samplesBuffer.ToArray()|]
                                                language = language
                                            }
                                    do! onTranscribe << TranscribeEnd <|
                                        {   mp3Writer = payload.mp3Writer
                                            vadTimings = payload.vadTimings
                                            audioDurationMs = payload.audioDurationMs
                                            transcriptions = transcriptions
                                        }
                                    }
                            | RecordFound payload ->
                                samplesBuffer.AddRange <| payload.audio.resampled.samples
                                let samples = samplesBuffer.ToArray()
                                if samples.Length > 0 then
                                    if tryAcquire lock then
                                        try
                                            let! transcriptions =
                                                transcribe
                                                    {   samplesBatch = [|samples|]
                                                        language = language
                                                    }
                                            do! onTranscribe << TranscribeFound <|
                                                {   mp3Writer = payload.mp3Writer
                                                    vadFrame = payload.vadFrame
                                                    audio = payload.audio
                                                    transcriptions = transcriptions
                                                }
                                        finally
                                            lockRelease lock
                    }
            do! consumer
        }
    Async.Start consumer
    { agent = agent }

let writeTranscriber = _.agent.AsyncAdd