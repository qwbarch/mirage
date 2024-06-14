module Mirage.Hook.Microphone

open FSharpPlus
open Silero.API
open Whisper.API
open NAudio.Wave
open Predictor.Domain
open Mirage.Domain.Logger
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Audio.Microphone.Recognition

type private MicrophoneSubscriber(whisper, silero, recordingDirectory) =
    let transcribeAudio (request: TranscribeRequest) =
        transcribe whisper
            {   samplesBatch = toPCMBytes <!> request.samplesBatch
                language = request.language
            }
    let transcriber = VoiceTranscriber transcribeAudio <| fun action ->
        async {
            match action with
                | TranscribeStart ->
                    logInfo "Transcription start"
                    let voiceActivityAtom =
                        VoiceActivityAtom
                            {   speakerId = Int StartOfRound.Instance.localPlayerController.playerSteamId
                                prob = 1.0
                            }
                    ()
                | TranscribeEnd payload ->
                    logInfo $"Transcription end. text: {payload.transcriptions[0].text}"
                | TranscribeFound payload ->
                    logInfo $"Transcription found. text: {payload.transcriptions[0].text}"
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