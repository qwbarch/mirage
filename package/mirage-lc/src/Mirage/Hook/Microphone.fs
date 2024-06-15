module Mirage.Hook.Microphone

open System
open Silero.API
open FSharpPlus
open Whisper.API
open NAudio.Wave
open Predictor.Domain
open Mirage.Domain.Logger
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Recognition
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Unity.Predictor
open Mirage.Core.Async.LVar

type private MicrophoneSubscriber(whisper, silero, recordingDirectory) =
    let transcribeAudio (request: TranscribeRequest) =
        transcribe whisper
            {   samplesBatch = toPCMBytes <!> request.samplesBatch
                language = request.language
            }
    let transcriber =
        let mutable sentenceId = Guid.NewGuid()
        VoiceTranscriber transcribeAudio <| fun action ->
            async {
                match action with
                    | TranscribeStart ->
                        logInfo "Transcription start"
                        Predictor.LocalPlayer.Register <|
                            VoiceActivityAtom
                                {   speakerId = Int StartOfRound.Instance.localPlayerController.playerSteamId
                                    prob = 1.0
                                }
                        sentenceId <- Guid.NewGuid()
                    | TranscribeEnd payload ->
                        logInfo $"Transcription end. text: {payload.transcriptions[0].text}"
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
                                        {   text = payload.transcriptions[0].text
                                            sentenceId = sentenceId
                                            elapsedMillis = payload.audioDurationMs
                                            transcriptionProb = float payload.transcriptions[0].avgLogProb
                                            nospeechProb = float payload.transcriptions[0].noSpeechProb
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
                    | TranscribeFound payload ->
                        logInfo $"Transcription found. text: {payload.transcriptions[0].text}"
                        let! enemies = accessLVar Predictor.Enemies List.ofSeq
                        let spokeAtom =
                            SpokeAtom
                                {   text = payload.transcriptions[0].text
                                    sentenceId = sentenceId
                                    transcriptionProb = float payload.transcriptions[0].avgLogProb
                                    nospeechProb = float payload.transcriptions[0].noSpeechProb
                                    elapsedMillis = payload.vadFrame.elapsedTime
                                }
                        Predictor.LocalPlayer.Register spokeAtom
                        flip iter enemies <| fun enemy ->
                            enemy.Register spokeAtom
                        Predictor.LocalPlayer.Register <|
                            HeardAtom
                                {   text = payload.transcriptions[0].text
                                    speakerClass = Predictor.LocalPlayer.SpeakerId
                                    speakerId = Predictor.LocalPlayer.SpeakerId
                                    sentenceId = sentenceId
                                    elapsedMillis = payload.vadFrame.elapsedTime
                                    transcriptionProb = float payload.transcriptions[0].avgLogProb
                                    nospeechProb = float payload.transcriptions[0].noSpeechProb
                                }
            }
    let recorder = Recorder recordingDirectory (writeTranscriber transcriber << Transcribe)
    let detector = VoiceDetector (result << detectSpeech silero) (writeRecorder recorder)
    let resampler = Resampler <| writeDetector detector

    interface Dissonance.Audio.Capture.IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            Async.StartImmediate <|
                writeResampler resampler
                    {   samples = buffer.ToArray()
                        format = WaveFormat(format.SampleRate, format.Channels)
                    }
        member _.Reset() = ()

let readMicrophone whisper silero recordingDirectory =
    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber(whisper, silero, recordingDirectory)
    )