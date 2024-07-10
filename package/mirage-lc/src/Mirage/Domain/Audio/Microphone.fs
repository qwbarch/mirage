module Mirage.Domain.Audio.Microphone

open System
open Silero.API
open FSharpPlus
open Whisper.API
open Predictor.Domain
open Mirage.Domain.Logger
open Mirage.Unity.Predictor
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Recognition
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.LVar
open Mirage.Core.Audio.File.Mp3Writer

type RequestStart =
    {   fileId: Guid
        playerId: uint64
        sentenceId: Guid
    }

type RequestEnd = { sentenceId: Guid }

type RequestFound =
    {   vadFrame: VADFrame
        playerId: uint64
        sentenceId: Guid
        samples: Samples 
    }

/// A request to transcribe via the host (from a non-host).
type RequestAction
    = RequestStart of RequestStart
    | RequestEnd of RequestEnd
    | RequestFound of RequestFound


type ResponsePayload =
    {   fileId: Guid
        sentenceId: Guid
        vadFrame: VADFrame
        transcription: Transcription
    }

/// A response containing the transcribed text from the host (to a non-host).
type ResponseAction
    = ResponseFound of ResponsePayload
    | ResponseEnd of ResponsePayload

let onTranscribe sentenceId (action: TranscribeLocalAction<Transcription>) =
    Async.Start <|
        async {
            match action with
                | TranscribeStart ->
                    logInfo "Transcription start"
                    Predictor.LocalPlayer.Register <|
                        VoiceActivityAtom
                            {   speakerId = Int StartOfRound.Instance.localPlayerController.playerSteamId
                                prob = 1.0
                            }
                    logInfo "Transcription start finished"
                | TranscribeEnd payload ->
                    logInfo $"Transcription end. text: {payload.transcription.text}"
                    let toVADTiming vadFrame =
                        let atom =
                            {   speakerId = Predictor.LocalPlayer.SpeakerId
                                prob = float vadFrame.probability
                            }
                        (vadFrame.elapsedTime, atom)
                    Predictor.LocalPlayer.Register <|
                        SpokeRecordingAtom
                            {   spokeAtom =
                                    {   text = payload.transcription.text
                                        sentenceId = sentenceId
                                        elapsedMillis = payload.vadFrame.elapsedTime
                                        transcriptionProb = float payload.transcription.avgLogProb
                                        nospeechProb = float payload.transcription.noSpeechProb
                                    }
                                whisperTimings = []
                                vadTimings = toVADTiming <!> payload.vadTimings
                                audioInfo =
                                    {   fileId = new Guid ""
                                        duration = payload.vadFrame.elapsedTime
                                    }
                            }
                | TranscribeFound payload ->
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
                                transcriptionProb = float payload.transcription.avgLogProb
                                nospeechProb = float payload.transcription.noSpeechProb
                            }
                    flip iter enemies <| fun enemy ->
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

type InitMicrophoneProcessor =
    {   recordingDirectory: string
        cudaAvailable: bool
        whisper: Whisper
        silero: SileroVAD
        /// Whether or not we are ready to process audio samples from the microphone.
        isReady: LVar<bool>
        /// Whether or not we should transcribe audio via the host instead of locally.
        transcribeViaHost: LVar<bool>
        /// A function that sends a request to the target player.
        sendRequest: uint64 -> RequestAction -> Async<unit>
        /// A function that sends a response to the target player.
        sendResponse: uint64 -> ResponseAction -> Async<unit>
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
            async {
                match action with
                    | TranscribeBatchedAction sentenceAction ->
                        match sentenceAction with
                            | TranscribeBatchedFound payload ->
                                do! param.sendResponse payload.playerId << ResponseFound <|
                                    {   fileId = payload.fileId
                                        sentenceId = payload.sentenceId
                                        vadFrame = payload.vadFrame
                                        transcription = payload.transcription
                                    }
                            | TranscribeBatchedEnd payload ->
                                do! param.sendResponse payload.playerId << ResponseEnd <|
                                    {   fileId = payload.fileId
                                        sentenceId = payload.sentenceId
                                        vadFrame = payload.vadFrame
                                        transcription = payload.transcription
                                    }
                    | TranscribeLocalAction transcribeAction ->
                        if transcribeAction = TranscribeStart then
                            sentenceId <- Guid.NewGuid()
                        onTranscribe sentenceId transcribeAction
            }

    let recorder =
        let mutable sentenceId = Guid.NewGuid()
        Recorder param.recordingDirectory <| fun action ->
            async {
                let! transcribeViaHost = readLVar param.transcribeViaHost
                if StartOfRound.Instance.IsHost || not transcribeViaHost then
                    do! writeTranscriber transcriber <| TranscribeLocal action
                else if transcribeViaHost then
                    logInfo "transcribing via host"
                    let sendRequest = param.sendRequest StartOfRound.Instance.localPlayerController.playerClientId
                    let playerId = StartOfRound.Instance.localPlayerController.playerClientId
                    match action with
                        | RecordStart payload ->
                            sentenceId <- Guid.NewGuid()
                            do! sendRequest << RequestStart <|
                                {   fileId = getFileId payload.mp3Writer
                                    playerId = playerId
                                    sentenceId = sentenceId
                                }
                        | RecordEnd _ ->
                            do! sendRequest <| RequestEnd { sentenceId = sentenceId }
                        | RecordFound payload ->
                            do! sendRequest << RequestFound <|
                                {   vadFrame = payload.vadFrame
                                    playerId = playerId
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

let processTranscriber processor = writeTranscriber processor.transcriber