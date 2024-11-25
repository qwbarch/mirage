module Mirage.Hook.Microphone

#nowarn "40"

open Dissonance
open Dissonance.Audio.Capture
open System
open Silero.API
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Domain.Config
open Mirage.Domain.Setting
open Mirage.Domain.Logger

let [<Literal>] SamplesPerWindow = 1024

[<Struct>]
type RecordState =
    {   samples: Samples
        format: WaveFormat
        isReady: bool
        isPlayerDead: bool
        pushToTalkEnabled: bool
        isMuted: bool
        allowRecordVoice: bool
    }

[<Struct>]
type ProcessingState =
    {   forcedProbability: Option<float32>
        allowRecordVoice: bool
    }

let private channel = new BlockingQueueAgent<ValueOption<RecordState>>(Int32.MaxValue)
let mutable private isReady = false

type MicrophoneSubscriber() =
    interface IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            if not <| isNull StartOfRound.Instance then
                Async.StartImmediate <<
                    channel.AsyncAdd << ValueSome <|
                        {   samples = buffer.ToArray()
                            format= WaveFormat(format.SampleRate, format.Channels)
                            isReady = isReady
                            isPlayerDead = StartOfRound.Instance.localPlayerController.isPlayerDead
                            pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
                            isMuted = getDissonance().IsMuted
                            allowRecordVoice = getSettings().allowRecordVoice
                        }
        member _.Reset() =
            logWarning "microphone reset"
            Async.StartImmediate <| channel.AsyncAdd ValueNone

let readMicrophone recordingDirectory =
    let silero = SileroVAD SamplesPerWindow
    let recorder =
        Recorder
            {   minAudioDurationMs = localConfig.MinAudioDurationMs.Value
                directory = recordingDirectory
                allowRecordVoice = _.allowRecordVoice
                onRecording = konst << konst <| result ()
            }
    let voiceDetector =
        VoiceDetector
            {   minSilenceDurationMs = localConfig.MinSilenceDurationMs.Value
                forcedProbability = _.forcedProbability
                detectSpeech = result << detectSpeech silero
                onVoiceDetected = curry <| writeRecorder recorder
            }
    let resampler = Resampler (writeDetector voiceDetector)
    let resample = Async.StartImmediate << writeResampler resampler
    let rec consumer =
        async {
            do! channel.AsyncGet() |>> function
                | ValueNone -> resample ResamplerInput.Reset
                | ValueSome state ->
                    if state.isReady && (getConfig().enableRecordVoiceWhileDead || not state.isPlayerDead) then
                        let frame =
                            {   samples = state.samples
                                format = state.format
                            }
                        let processingState =
                            {   forcedProbability =
                                    if state.isMuted then Some 0f
                                    else if state.pushToTalkEnabled then Some 1f
                                    else None
                                allowRecordVoice = getSettings().allowRecordVoice
                            }
                        resample <| ResamplerInput (processingState, frame)
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
        isReady <- true
        orig.Invoke self
    )

    // Set isReady: false when exiting to the main menu, or the round is over.
    On.StartOfRound.add_OnDestroy(fun orig self ->
        isReady <- false
        orig.Invoke self
    )
    On.StartOfRound.add_ReviveDeadPlayers(fun orig self ->
        isReady <- false
        orig.Invoke self
    )