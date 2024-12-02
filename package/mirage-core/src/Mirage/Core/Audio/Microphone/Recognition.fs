module Mirage.Core.Audio.Microphone.Recognition

//#nowarn "40"
//
//open System
//open System.Collections.Generic
//open FSharpPlus
//open FSharpx.Control
//open Mirage.Prelude
//open Mirage.Core.Async.Lock
//open Mirage.Core.Audio.PCM
//open Mirage.Core.Audio.Microphone.Resampler
//open Mirage.Core.Audio.Microphone.Recorder
//open Mirage.Core.Audio.Microphone.Detection
//
///// State of a transcription for a batched transcription job.
//type private BatchedState<'PlayerId> =
//    {   fileId: Guid
//        playerId: 'PlayerId
//        /// Current samples to be transcribed.
//        samples: List<float32>
//        /// Vad timing for the latest frame.
//        mutable vadFrame: VADFrame
//        /// Full VAD timings. Empty until the state is finished.
//        mutable vadTimings: list<VADFrame>
//        /// Whether or not the transcription should be the final one for this sentence.
//        mutable finished: bool
//    }
//
///// Data required for when the transcriber should start transcribing another player's audio.
//type BatchedStart<'PlayerId> =
//    {   fileId: Guid
//        playerId: 'PlayerId
//        sentenceId: Guid
//    }
//
///// Samples that should be transcribed for another player.
//type BatchedFound<'PlayerId> =
//    {   playerId: 'PlayerId
//        sentenceId: Guid
//        samples: Samples
//        vadFrame: VADFrame
//    }
//
///// Action to run when a transcription is finished processing for another player.
//type BatchedEnd =
//    {   sentenceId: Guid
//        vadTimings: list<VADFrame>
//    }
//
///// Audio samples that should be transcribed as a sentence.
//type TranscribeBatched<'PlayerId>
//    = BatchedStart of BatchedStart<'PlayerId>
//    | BatchedFound of BatchedFound<'PlayerId>
//    | BatchedEnd of BatchedEnd
//
//type TranscriberInput<'PlayerId> =
//    /// Transcribe audio for the local player.
//    | TranscribeLocal of RecordAction
//    /// Transcribe audio from another player.
//    | TranscribeBatched of TranscribeBatched<'PlayerId>
//    /// Attempts to transcribe batched audio previously set by <b>TranscribeBatched</b>.
//    /// If the lock is acquired, this will simply do nothing.
//    | TryTranscribeAudio
//    /// Language that should be used during inference for all transcriptions.
//    /// While WhisperS2T does support setting per-transcription languages, this is simplified
//    /// to be set for all transcriptions, since everyone in a single lobby should be using the same language anyways.
//    | SetLanguage of string
//
///// A batch of samples that should be transcribed into text.
//type TranscribeRequest =
//    {   samplesBatch: Samples[]
//        language: string
//    }
//
///// This action only runs when samples are available (array length is > 0).
//type TranscribeFound<'Transcription> =
//    {   fileId: Guid
//        vadFrame: VADFrame
//        transcription: 'Transcription
//    }
//
///// The transcription is finished.
//type TranscribeEnd<'Transcription> =
//    {   fileId: Guid
//        vadFrame: VADFrame
//        vadTimings: list<VADFrame>
//        transcription: 'Transcription
//    }
//
///// A sum type representing stages of transcribing a recording.
//type TranscribeLocalAction<'Transcription>
//    = TranscribeStart
//    | TranscribeFound of TranscribeFound<'Transcription>
//    | TranscribeEnd of TranscribeEnd<'Transcription>
//
///// A transcription of the currently processed samples.
//type TranscribeBatchedFound<'PlayerId, 'Transcription> =
//    {   fileId: Guid
//        playerId: 'PlayerId
//        sentenceId: Guid
//        vadFrame: VADFrame
//        transcription: 'Transcription
//    }
//
///// A transcription of the full recording.
//type TranscribeBatchedEnd<'PlayerId, 'Transcription> =
//    {   fileId: Guid
//        playerId: 'PlayerId
//        sentenceId: Guid
//        vadFrame: VADFrame
//        vadTimings: list<VADFrame>
//        transcription: 'Transcription
//    }
//
///// A sum type representing stages of transcribing a sentence.
//type TranscribeBatchedAction<'PlayerId, 'Transcription>
//    = TranscribeBatchedFound of TranscribeBatchedFound<'PlayerId, 'Transcription>
//    | TranscribeBatchedEnd of TranscribeBatchedEnd<'PlayerId, 'Transcription>
//
///// This action is run whenever a transcription is available.
//type TranscribeAction<'PlayerId, 'Transcription>
//    = TranscribeLocalAction of TranscribeLocalAction<'Transcription>
//    | TranscribeBatchedAction of TranscribeBatchedAction<'PlayerId, 'Transcription>
//
//// Transcribe voice audio into text.
//type VoiceTranscriber<'PlayerId> =
//    private
//        {   agent: BlockingQueueAgent<TranscriberInput<'PlayerId>>
//        }
//    interface IDisposable with
//        member this.Dispose() = dispose this.agent
//
//let VoiceTranscriber<'PlayerId, 'Transcription>
//    (transcribe: TranscribeRequest -> Async<'Transcription[]>)
//    (onTranscribe: TranscribeAction<'PlayerId, 'Transcription> -> Async<Unit>) =
//        let agent = new BlockingQueueAgent<TranscriberInput<'PlayerId>>(Int32.MaxValue)
//        let lock = createLock()
//        let mutable language = "en"
//        let mutable batchedStates: Map<Guid, BatchedState<'PlayerId>> = Map.empty
//
//        /// Attempts to batch transcription jobs if the sentences map contains samples.
//        /// If a batch job is done, this will also run the appropriate TranscribeSentenceAction for it as well.
//        let transcribeAudio (samples: Samples) =
//            async {
//                // WhisperS2T processes an array of samples.
//                // In order to know which index is the host's transriptions vs non-host transcriptions,
//                // a temporary "hostId" is added to the map, along with its current audio samples.
//                let hostId = Guid.NewGuid()
//                let samplesMap =
//                    flip Map.mapValues batchedStates _.samples
//                        |> Map.add hostId (List samples)
//                        |> Map.filter (fun _ value -> value.Count > 0)
//                let sentenceIds = Array.ofSeq <| Map.keys samplesMap
//                let samples: Samples[] =
//                    Map.values samplesMap
//                        |> map Array.ofSeq
//                        |> Array.ofSeq
//                if samples.Length > 0 then
//                    let! transcriptions =
//                        transcribe
//                            {   samplesBatch = samples
//                                language = language
//                            }
//                    if sentenceIds.Length > 0 then
//                        for i in 0 .. transcriptions.Length - 1 do
//                            let sentenceId = sentenceIds[i]
//                            if sentenceId <> hostId then
//                                let sentence = batchedStates[sentenceId]
//                                if sentence.finished then
//                                    &batchedStates %= Map.remove sentenceId
//                                    Async.StartImmediate << onTranscribe << TranscribeBatchedAction << TranscribeBatchedEnd <|
//                                        {   fileId = sentence.fileId
//                                            playerId = sentence.playerId
//                                            sentenceId = sentenceId
//                                            vadFrame = sentence.vadFrame
//                                            vadTimings = sentence.vadTimings
//                                            transcription = transcriptions[i]
//                                        }
//                                else
//                                    Async.StartImmediate << onTranscribe << TranscribeBatchedAction << TranscribeBatchedFound <|
//                                        {   fileId = sentence.fileId
//                                            playerId = sentence.playerId
//                                            sentenceId = sentenceId
//                                            vadFrame = sentence.vadFrame
//                                            transcription = transcriptions[i]
//                                        }
//                    return flip map (Array.tryFindIndex ((=) hostId) sentenceIds) <| fun index ->
//                        //printfn "host transcription finished"
//                        transcriptions[index]
//                else
//                    return None
//            }
//        let tryTranscribeAudio samples =
//            async {
//                let mutable value = None
//                if tryAcquire lock then
//                    try
//                        let! transcription = transcribeAudio samples
//                        value <- transcription
//                    finally
//                        lockRelease lock
//                else
//                    printfn "lock failed to acquire"
//                return value
//            }
//        let rec consumer =
//            async {
//                let! input = agent.AsyncGet()
//                match input with
//                    | SetLanguage lang -> language <- lang
//                    | TryTranscribeAudio -> Async.StartImmediate << map ignore <| tryTranscribeAudio zero
//                    | TranscribeBatched action ->
//                        match action with
//                            | BatchedStart payload ->
//                                &batchedStates %=
//                                    Map.add payload.sentenceId
//                                        {   fileId = payload.fileId
//                                            playerId = payload.playerId
//                                            vadTimings = []
//                                            vadFrame =
//                                                {   elapsedTime = 0
//                                                    probability = 0f
//                                                }
//                                            samples = zero
//                                            finished = false
//                                        }
//                            | BatchedEnd payload ->
//                                match Map.tryFind payload.sentenceId batchedStates with
//                                    | None -> ()
//                                    | Some sentence ->
//                                        sentence.vadTimings <- payload.vadTimings
//                                        sentence.finished <- true
//                                        do! ignore <!> tryTranscribeAudio zero
//                            | BatchedFound payload ->
//                                match Map.tryFind payload.sentenceId batchedStates with
//                                    | None -> ()
//                                    | Some sentence ->
//                                        if payload.samples.Length > 0 then
//                                            sentence.vadFrame <- payload.vadFrame
//                                            sentence.samples.AddRange payload.samples
//                                            do! ignore <!> tryTranscribeAudio zero
//                    | TranscribeLocal action ->
//                        match action with
//                            | RecordStart _ ->
//                                Async.StartImmediate << onTranscribe <| TranscribeLocalAction TranscribeStart
//                            | RecordEnd payload ->
//                                Async.StartImmediate << withLock' lock <| async {
//                                    let! transcription = transcribeAudio payload.fullAudio.resampled.samples
//                                    Async.StartImmediate << onTranscribe << TranscribeLocalAction << TranscribeEnd <|
//                                        {   fileId = getFileId payload.mp3Writer
//                                            vadFrame = payload.vadFrame
//                                            vadTimings = payload.vadTimings
//                                            transcription = transcription.Value
//                                        }
//                                    }
//                            | RecordFound payload ->
//                                ()
//                                //Async.StartImmediate << map ignore << OptionT.run <| monad {
//                                //    let samples = payload.fullAudio.resampled.samples
//                                //    if samples.Length > 0 then
//                                //        let! transcription = OptionT <| tryTranscribeAudio samples
//                                //        do! OptionT.lift << onTranscribe << TranscribeLocalAction << TranscribeFound <|
//                                //            {   fileId = getFileId payload.mp3Writer
//                                //                vadFrame = payload.vadFrame
//                                //                transcription = transcription
//                                //            }
//                                //}
//                do! consumer
//            }
//        Async.Start consumer
//        { agent = agent }
//
//let writeTranscriber = _.agent.AsyncAdd