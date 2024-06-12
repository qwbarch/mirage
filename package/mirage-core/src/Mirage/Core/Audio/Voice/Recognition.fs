module Mirage.Core.Audio.Voice.Recognition

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Core.Audio.Resampler

/// A sum type representing stages of a live transcription.
type TranscribeAction
    = TranscribeStart
    | TranscribeFound
    | TranscribeEnd

/// A function that executes whenever a transcription finishes.
type OnTranscribe = TranscribeAction -> Async<Unit>

/// Data to be passed to the transcriber.
type TranscriptionInput =
    {   audio: ResampledAudio
        waveFormat: WaveFormat
        mp3Writer: Mp3Writer
    }

// Transcribe voice audio into text.
type VoiceTranscriber =
    private { agent: BlockingQueueAgent<TranscriptionInput> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let VoiceTranscriber (onTranscribe: OnTranscribe) =
    let agent = new BlockingQueueAgent<TranscriptionInput>(Int32.MaxValue)
    let rec consumer =
        async {
            let! input = agent.AsyncGet()
            //if input.samples.Length > 0 then
            //    ()
            do! consumer
        }
    { agent = agent }