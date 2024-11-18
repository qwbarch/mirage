module Mirage.Hook.Microphone

#nowarn "40"

open FSharpx.Control
open Dissonance
open Dissonance.VAD
open Dissonance.Audio.Capture
open System
open System.Collections.Generic
open NAudio.Wave
open NAudio.Lame
open Mirage.Prelude
open Mirage.Domain.Config
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.File.Mp3Writer
open FSharpPlus
open Mirage.Domain.Logger

[<Struct>]
type RecordState =
    {   samples: Samples
        waveFormat: WaveFormat
        isReady: bool
        isPlayerDead: bool
        pushToTalkEnabled: bool
        isMuted: bool
        micEnabled: bool
        speechDetected: bool
    }

let private channel = new BlockingQueueAgent<RecordState>(Int32.MaxValue)

/// Mutable state, should only be accessed via the unity thread.
let mutable private _isReady = false
let mutable private _speechDetected = false

/// By default, dissonance sends us 10ms audio frames at a time.
/// VAD can be switched off abruptly. The recording is closed after the defined milliseconds of audio contains no speech detected.
let private minSilenceDurationMs = 150

/// Only keep recordings that contains at least the defined milliseconds of audio.
let private minAudioDurationMs = 300

type MicrophoneSubscriber() =
    interface IVoiceActivationListener with
        member _.VoiceActivationStart() = _speechDetected <- true
        member _.VoiceActivationStop() = _speechDetected <- false
    interface IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            if not <| isNull StartOfRound.Instance then
                Async.StartImmediate <<
                    channel.AsyncAdd <|
                        {   samples = buffer.ToArray()
                            waveFormat = WaveFormat(format.SampleRate, format.Channels)
                            isReady = _isReady
                            isPlayerDead = StartOfRound.Instance.localPlayerController.isPlayerDead
                            pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
                            isMuted = getDissonance().IsMuted
                            micEnabled = IngamePlayerSettings.Instance.settings.micEnabled
                            speechDetected = _speechDetected
                        }
        member _.Reset() =
            Async.StartImmediate <<
                channel.AsyncAdd <|
                    {   samples = zero
                        waveFormat = WaveFormat(0, 0)
                        isReady = zero
                        isPlayerDead = zero
                        pushToTalkEnabled = zero
                        isMuted = zero
                        micEnabled = zero
                        speechDetected = zero
                    }

let private shouldRecord state =
    let pushToTalkPressed = state.pushToTalkEnabled && not state.isMuted
    (getConfig().enableRecordVoiceWhileDead || not state.isPlayerDead) && (pushToTalkPressed || not state.pushToTalkEnabled)

let readMicrophone recordingDirectory =
    let mutable isRecording = false
    let mutable silentFrames = 0
    let mutable framesWritten = 0
    let mutable samples = List<float32>()
    let reset () =
        samples.Clear()
        isRecording <- false
    let rec consumer =
        async {
            let! state = channel.AsyncGet()
            if state.isReady then
                if state.speechDetected then
                    silentFrames <- 0
                    &framesWritten += 1
                else
                    &silentFrames += 1
                if shouldRecord state then
                    if not isRecording then
                        isRecording <- true
                        framesWritten <- 0
                    samples.AddRange state.samples
                    logInfo $"recording. silentFrames: {silentFrames} framesWritten: {framesWritten}"
                else
                    if silentFrames >= minSilenceDurationMs / 10 && framesWritten >= minAudioDurationMs / 10 then
                        use! mp3Writer = createMp3Writer recordingDirectory state.waveFormat LAMEPreset.STANDARD
                        do! writeMp3File mp3Writer <| samples.ToArray()
                    reset()
            else reset()

            do! consumer
        }
    Async.Start consumer

    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber()
    )

    // Normally during the opening doors sequence, the game suffers from dropped audio frames, causing recordings to sound glitchy.
    // To reduce the likelihood of recording glitched sounds, audio recordings only start after the sequence is completely finished.
    On.StartOfRound.add_StartTrackingAllPlayerVoices(fun orig self ->
        _isReady <- true
        orig.Invoke self
    )

    // Set isReady: false when exiting to the main menu, or the round is over.
    On.StartOfRound.add_OnDestroy(fun orig self ->
        _isReady <- false
        orig.Invoke self
    )
    On.StartOfRound.add_ReviveDeadPlayers(fun orig self ->
        _isReady <- false
        orig.Invoke self
    )