module Mirage.Core.Audio.Microphone.Recognition

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open Mirage.Prelude
open Mirage.Core.Async.Lock
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Detection

/// State of a transcription for a sentence.
type private SentenceState<'PlayerId> =
    {   playerId: 'PlayerId
        /// Current samples to be transcribed.
        samples: List<float32>
        /// Whether or not the transcription should be the final one for this sentence.
        mutable finished: bool
    }

/// Data required for when the transcriber should start a new sentence.
type SentenceStart<'PlayerId> =
    {   playerId: 'PlayerId
        sentenceId: Guid
    }

/// Samples that should be transcribed.
type SentenceFound<'PlayerId> =
    {   playerId: 'PlayerId
        sentenceId: Guid
        samples: Samples
    }

/// Identifier required to finish a transcription for the given sentence.
type SentenceEnd = { sentenceId: Guid }

/// Audio samples that should be transcribed as a sentence.
type TranscribeSentence<'PlayerId>
    = SentenceStart of SentenceStart<'PlayerId>
    | SentenceFound of SentenceFound<'PlayerId>
    | SentenceEnd of SentenceEnd

type TranscriberInput<'PlayerId> =
    /// Transcribe audio from a recording (in-progress).
    | TranscribeRecording of RecordAction
    /// Transcribe audio from a given set of samples, as a single sentence.
    | TranscribeSentence of TranscribeSentence<'PlayerId>
    /// Language that should be used during inference for all transcriptions.
    /// While WhisperS2T does support setting per-transcription languages, this is simplified
    /// to be set for all transcriptions, since everyone in a single lobby should be using the same language anyways.
    | SetLanguage of string

/// A batch of samples that should be transcribed into text.
type TranscribeRequest =
    {   samplesBatch: Samples[]
        language: string
    }

/// This action only runs when samples are available (array length is > 0).
type TranscribeRecordingFound<'Transcription> =
    {   mp3Writer: Mp3Writer
        vadFrame: VADFrame
        audio: ResampledAudio
        transcription: 'Transcription
    }

/// The transcription is finished.
type TranscribeRecordingEnd<'Transcription> =
    {   mp3Writer: Mp3Writer
        vadTimings: list<VADFrame>
        audioDurationMs: int
        transcription: 'Transcription
    }

/// A sum type representing stages of transcribing a recording.
type TranscribeRecordingAction<'Transcription>
    = TranscribeRecordingStart
    | TranscribeRecordingFound of TranscribeRecordingFound<'Transcription>
    | TranscribeRecordingEnd of TranscribeRecordingEnd<'Transcription>

/// A transcription of the currently processed samples.
type TranscribeSentenceFound<'PlayerId, 'Transcription> =
    {   playerId: 'PlayerId
        sentenceId: Guid
        transcription: 'Transcription
    }

/// A transcription of the full recording.
type TranscribeSentenceEnd<'PlayerId, 'Transcription> =
    {   playerId: 'PlayerId
        sentenceId: Guid
        transcription: 'Transcription
    }

/// A sum type representing stages of transcribing a sentence.
type TranscribeSentenceAction<'PlayerId, 'Transcription>
    = TranscribeSentenceFound of TranscribeSentenceFound<'PlayerId, 'Transcription>
    | TranscribeSentenceEnd of TranscribeSentenceEnd<'PlayerId, 'Transcription>

/// This action is run whenever a transcription is available.
type TranscribeAction<'PlayerId, 'Transcription>
    = TranscribeRecordingAction of TranscribeRecordingAction<'Transcription>
    | TranscribeSentenceAction of TranscribeSentenceAction<'PlayerId, 'Transcription>

// Transcribe voice audio into text.
type VoiceTranscriber<'PlayerId> =
    private { agent: BlockingQueueAgent<TranscriberInput<'PlayerId>> }
    interface IDisposable with
        member this.Dispose() = dispose this.agent

let VoiceTranscriber<'PlayerId, 'Transcription>
    (transcribe: TranscribeRequest -> Async<'Transcription[]>)
    (onTranscribe: TranscribeAction<'PlayerId, 'Transcription> -> Async<Unit>) =
        let agent = new BlockingQueueAgent<TranscriberInput<'PlayerId>>(Int32.MaxValue)
        let lock = createLock()
        let sampleBuffer = new List<float32>()
        let mutable language = "en"
        let mutable sentences: Map<Guid, SentenceState<'PlayerId>> = Map.empty

        /// Attempts to batch transcription jobs if the sentences map contains samples.
        /// If a batch job is done, this will also run the appropriate TranscribeSentenceAction for it as well.
        let transcribeAudio =
            async {
                // WhisperS2T processes an array of samples.
                // In order to know which index is the host's transriptions vs non-host transcriptions,
                // a temporary "hostId" is added to the map, along with its current audio samples.
                let hostId = Guid.NewGuid()
                let samplesMap =
                    flip Map.mapValues sentences _.samples
                        |> Map.add hostId sampleBuffer
                        |> Map.filter (fun _ value -> value.Count > 0)
                printfn $"# of sentences: {samplesMap.Count}"
                let sentenceIds = Array.ofSeq <| Map.keys samplesMap
                let samples: Samples[] =
                    Map.values samplesMap
                        |> map Array.ofSeq
                        |> Array.ofSeq
                printfn $"samples length: {samples.Length}"
                for i in 0 .. samples.Length - 1 do
                    printfn $"samples[{i}].Length: {samples[i].Length}"
                printfn "before transcriptions (mirage.core)"
                if samples.Length > 0 then
                    printfn $"before transcribe. samplesBatch.Length: {samples.Length}"
                    for i in 0 .. samples.Length - 1 do
                        printfn $"samplesBatch[{i}].Length: {samples[i].Length}"
                    let! transcriptions =
                        transcribe
                            {   samplesBatch = samples
                                language = language
                            }
                    printfn $"after transcriptions (mirage.core). length: {transcriptions.Length}. sentenceIds.Length: {sentenceIds.Length}"
                    if sentenceIds.Length > 0 then
                        printfn "inside sentenceIds if statement"
                        for i in 0 .. transcriptions.Length - 1 do
                            printfn "inside for loop"
                            printfn $"index: {i}. sentenceId: {sentenceIds[i]}"
                            if sentenceIds[i] = hostId then
                                printfn "sentenceId is the host" // TODO DELETE THIS IF
                            else if sentenceIds[i] <> hostId then
                                printfn "sentenceId is not the host"
                                let sentence = sentences[sentenceIds[i]]
                                if sentence.finished then
                                    printfn "state finished. removing sentence"
                                    &sentences %= Map.remove sentenceIds[i]
                                    do! onTranscribe << TranscribeSentenceAction << TranscribeSentenceEnd <|
                                        {   playerId = sentence.playerId
                                            sentenceId = sentenceIds[i]
                                            transcription = transcriptions[i]
                                        }
                                else
                                    printfn "state in progress."
                                    do! onTranscribe << TranscribeSentenceAction << TranscribeSentenceFound <|
                                        {   playerId = sentence.playerId
                                            sentenceId = sentenceIds[i]
                                            transcription = transcriptions[i]
                                        }
                    printfn "before return"
                    return flip map (Array.tryFindIndex ((=) hostId) sentenceIds) <| fun index ->
                        printfn "host transcription finished"
                        transcriptions[index]
                else
                    return None
            }
        let rec consumer =
            async {
                let! input = agent.AsyncGet()
                match input with
                    | SetLanguage lang -> language <- lang
                    | TranscribeSentence action ->
                        let tryTranscribeAudio =
                            Async.StartImmediate <|
                                async {
                                    if tryAcquire lock then
                                        printfn "lock acquired"
                                        try do! ignore <!> transcribeAudio
                                        finally
                                            lockRelease lock
                                            printfn "lock released"
                                    else
                                        printfn "lock failed to acquire"
                                }
                        match action with
                            | SentenceStart payload ->
                                printfn $"SentenceStart (mirage.core): {payload.sentenceId}"
                                printfn $"Sentences (before modify): {sentences.Count}"
                                &sentences %=
                                    Map.add payload.sentenceId
                                        {   playerId = payload.playerId
                                            samples = new List<float32>()
                                            finished = false
                                        }
                                printfn $"Sentences (after modify): {sentences.Count}"
                            | SentenceEnd payload ->
                                printfn $"SentenceEnd (mirage.core): {payload.sentenceId}"
                                match Map.tryFind payload.sentenceId sentences with
                                    | None -> ()
                                    | Some sentence ->
                                        sentence.finished <- true
                                        printfn "before transcribeAudio SentenceEnd"
                                        tryTranscribeAudio
                            | SentenceFound payload ->
                                printfn $"SentenceFound (mirage.core): {payload.sentenceId}"
                                match Map.tryFind payload.sentenceId sentences with
                                    | None -> ()
                                    | Some sentence ->
                                        if payload.samples.Length > 0 then
                                            sentence.samples.AddRange payload.samples
                                            printfn "before transcribeAudio SentenceFound"
                                            tryTranscribeAudio
                    | TranscribeRecording action ->
                        match action with
                            | RecordStart _ ->
                                sampleBuffer.Clear()
                                Async.StartImmediate << onTranscribe <| TranscribeRecordingAction TranscribeRecordingStart
                            | RecordEnd payload ->
                                Async.StartImmediate << withLock' lock <| async {
                                    printfn "before transcribeAudio RecordEnd"
                                    let! transcription = transcribeAudio
                                    printfn "after transcribeAudio RecordEnd"
                                    Async.StartImmediate << onTranscribe << TranscribeRecordingAction << TranscribeRecordingEnd <|
                                        {   mp3Writer = payload.mp3Writer
                                            vadTimings = payload.vadTimings
                                            audioDurationMs = payload.audioDurationMs
                                            transcription = transcription.Value
                                        }
                                    }
                            | RecordFound payload ->
                                sampleBuffer.AddRange <| payload.audio.resampled.samples
                                let samples = sampleBuffer.ToArray()
                                if samples.Length > 0 then
                                    if tryAcquire lock then
                                        try
                                            Async.StartImmediate <| async {
                                                printfn "before transcribeAudio RecordFound"
                                                let! transcription = transcribeAudio
                                                printfn "after transcribeAudio RecordFound"
                                                do! onTranscribe << TranscribeRecordingAction << TranscribeRecordingFound <|
                                                    {   mp3Writer = payload.mp3Writer
                                                        vadFrame = payload.vadFrame
                                                        audio = payload.audio
                                                        transcription = transcription.Value
                                                    }
                                            }
                                        finally
                                            lockRelease lock
                do! consumer
            }
        Async.Start consumer
        { agent = agent }

let writeTranscriber = _.agent.AsyncAdd