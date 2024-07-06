module Mirage.Domain.Audio.Microphone

open System
open System.Collections
open StaticNetcodeLib
open Silero.API
open Unity.Netcode
open FSharpPlus
open Whisper.API
open NAudio.Wave
open Mirage.Domain.Logger
open Mirage.Unity.Predictor
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Recognition
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.LVar
open Predictor.Domain
open Mirage.Core.Audio.File.Mp3Writer

type RemoteStart =
    {   playerId: uint64
        sentenceId: Guid
    }

type RemoteEnd = { sentenceId: Guid }

type RemoteFound =
    {   playerId: uint64
        sentenceId: Guid
        samples: Samples 
    }

type RemoteAction
    = RemoteStart of RemoteStart
    | RemoteEnd of RemoteEnd
    | RemoteFound of RemoteFound

/// THIS IS TEMPORARY WHILE I TEST THINGS
type SentencePayload =
    {   sentenceId: Guid
        text: string
        avgLogProb: float32
        noSpeechProb: float32
    }

/// THIS IS TEMPORARY WHILE I TEST THINGS
type RemoteSentenceAction
    = RemoteSentenceFound of SentencePayload
    | RemoteSentenceEnd of SentencePayload

type InitMicrophoneProcessor =
    {   recordingDirectory: string
        cudaAvailable: bool
        whisper: Whisper
        silero: SileroVAD
        /// Whether or not we are ready to process audio samples from the microphone.
        isReady: LVar<bool>
        /// Whether or not we should transcribe audio via the host instead of locally.
        transcribeViaHost: LVar<bool>
        /// A function that runs a remote action for the given player id.
        remoteAction: uint64 -> RemoteAction -> Async<unit>
        /// A function that runs a sentence action for the given player id.
        sentenceAction: uint64 -> RemoteSentenceAction -> Async<unit>
    }

type MicrophoneProcessor =
    private
        {   resampler: Resampler
            transcriber: VoiceTranscriber<uint64>
        }

let MicrophoneProcessor param =
    let transcribeAudio(request: TranscribeRequest) =
        async {
            logInfo "transcribing audio"
            return!
                transcribe param.whisper
                    {   samplesBatch = toPCMBytes <!> request.samplesBatch
                        language = request.language
                    }
        }

    let transcriber =
        let mutable sentenceId = Guid.NewGuid()
        VoiceTranscriber<uint64, Transcription> transcribeAudio <| fun action ->
            //match action with
            //    | TranscribeRecordingAction action ->
            //        async {
            //            match action with
            //                | TranscribeRecordingStart ->
            //                    logInfo "TranscribeRecordingStart"
            //                | TranscribeRecordingEnd payload ->
            //                    logInfo $"TranscribeRecordingEnd. Text: {payload.transcription.text}"
            //                | TranscribeRecordingFound payload  ->
            //                    //logInfo $"TranscribeRecordingFound. Text: {payload.transcription.text}"
            //                    ()
            //        }
            //    | TranscribeSentenceAction action ->
            //        async {
            //            match action with
            //                | TranscribeSentenceFound payload ->
            //                    logInfo "TranscribeSentenceFound (mirage.lc)"
            //                    do! param.sentenceAction payload.playerId << RemoteSentenceFound <|
            //                        {   sentenceId = payload.sentenceId
            //                            text = payload.transcription.text
            //                            avgLogProb = payload.transcription.avgLogProb
            //                            noSpeechProb = payload.transcription.noSpeechProb
            //                        }
            //                | TranscribeSentenceEnd payload ->
            //                    logInfo "TranscribeSentenceEnd (mirage.lc)"
            //                    do! param.sentenceAction payload.playerId << RemoteSentenceEnd <|
            //                        {   sentenceId = payload.sentenceId
            //                            text = payload.transcription.text
            //                            avgLogProb = payload.transcription.avgLogProb
            //                            noSpeechProb = payload.transcription.noSpeechProb
            //                        }
            //        }

            async {
                match action with
                    | TranscribeSentenceAction _ -> ()
                    | TranscribeRecordingAction recordingAction ->
                        match recordingAction with
                            | TranscribeRecordingStart ->
                                logInfo "Transcription start"
                                Predictor.LocalPlayer.Register <|
                                    VoiceActivityAtom
                                        {   speakerId = Int StartOfRound.Instance.localPlayerController.playerSteamId
                                            prob = 1.0
                                        }
                                sentenceId <- Guid.NewGuid()
                                logInfo "Transcription start finished"
                            | TranscribeRecordingEnd payload ->
                                logInfo $"Transcription end. text: {payload.transcription.text}"
                                let! enemies = accessLVar Predictor.Enemies List.ofSeq
                                let toVADTiming vadFrame =
                                    let atom =
                                        {   speakerId = Predictor.LocalPlayer.SpeakerId
                                            prob = float vadFrame.probability
                                        }
                                    (vadFrame.elapsedTime, atom)
                                let spokeRecordingAtom =
                                    SpokeRecordingAtom
                                        {   spokeAtom =
                                                {   text = payload.transcription.text
                                                    sentenceId = sentenceId
                                                    elapsedMillis = payload.audioDurationMs
                                                    transcriptionProb = float payload.transcription.avgLogProb
                                                    nospeechProb = float payload.transcription.noSpeechProb
                                                }
                                            whisperTimings = []
                                            vadTimings = toVADTiming <!> payload.vadTimings
                                            audioInfo =
                                                {   fileId = getFileId payload.mp3Writer
                                                    duration = payload.audioDurationMs
                                                }
                                        }
                                Predictor.LocalPlayer.Register spokeRecordingAtom
                                flip iter enemies <| fun enemy ->
                                    enemy.Register spokeRecordingAtom
                            | TranscribeRecordingFound payload ->
                                logInfo $"Transcription found. text: {payload.transcription.text}"
                                let! enemies = accessLVar Predictor.Enemies List.ofSeq
                                let spokeAtom =
                                    SpokeAtom
                                        {   text = payload.transcription.text
                                            sentenceId = sentenceId
                                            transcriptionProb = float payload.transcription.avgLogProb
                                            nospeechProb = float payload.transcription.noSpeechProb
                                            elapsedMillis = payload.vadFrame.elapsedTime
                                        }
                                Predictor.LocalPlayer.Register spokeAtom
                                let heardAtom =
                                    HeardAtom
                                        {   text = payload.transcription.text
                                            speakerClass = Predictor.LocalPlayer.SpeakerId
                                            speakerId = Predictor.LocalPlayer.SpeakerId
                                            sentenceId = sentenceId
                                            elapsedMillis = payload.vadFrame.elapsedTime
                                            transcriptionProb = float payload.transcription.noSpeechProb
                                            nospeechProb = float payload.transcription.noSpeechProb
                                        }
                                flip iter enemies <| fun enemy ->
                                    enemy.Register spokeAtom
                                    enemy.Register heardAtom
                                Predictor.LocalPlayer.Register <|
                                    HeardAtom
                                        {   text = payload.transcription.text
                                            speakerClass = Predictor.LocalPlayer.SpeakerId
                                            speakerId = Predictor.LocalPlayer.SpeakerId
                                            sentenceId = sentenceId
                                            elapsedMillis = payload.vadFrame.elapsedTime
                                            transcriptionProb = float payload.transcription.avgLogProb
                                            nospeechProb = float payload.transcription.noSpeechProb
                                        }
            }

    let recorder =
        let mutable sentenceId = Guid.NewGuid()
        Recorder param.recordingDirectory <| fun action ->
            async {
                let! transcribeViaHost = readLVar param.transcribeViaHost
                if StartOfRound.Instance.IsHost || not transcribeViaHost then
                    do! writeTranscriber transcriber <| TranscribeRecording action
                else if transcribeViaHost then
                    logInfo "transcribing via host"
                    let remoteAction = param.remoteAction StartOfRound.Instance.localPlayerController.playerClientId
                    let playerId = StartOfRound.Instance.localPlayerController.playerClientId
                    match action with
                        | RecordStart _ ->
                            sentenceId <- Guid.NewGuid()
                            do! remoteAction << RemoteStart <|
                                {   playerId = playerId
                                    sentenceId = sentenceId
                                }
                        | RecordEnd _ ->
                            do! remoteAction <| RemoteEnd { sentenceId = sentenceId }
                        | RecordFound payload ->
                            do! remoteAction << RemoteFound <|
                                {   playerId = playerId
                                    sentenceId = sentenceId
                                    samples = payload.currentAudio.resampled.samples
                                }
            }
    let detector = VoiceDetector (result << detectSpeech param.silero) <| fun action ->
        async {
            let! isReady = readLVar param.isReady
            if isReady then
                do! writeRecorder recorder action
        }
    {   resampler = Resampler <| writeDetector detector
        transcriber = transcriber
    }

/// Feed an audio frame from the microphone to be processed.
let processMicrophone processor = writeResampler processor.resampler

/// Send an action for the microphone processor's transcriber to be processed.
let processTranscriber processor = writeTranscriber processor.transcriber