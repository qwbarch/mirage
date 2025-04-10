module Mirage.Hook.Microphone

open Dissonance
open Dissonance.Audio.Capture
open System
open System.Buffers
open System.Threading
open Silero.API
open IcedTasks
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Loop
open Mirage.Core.Task.Fork
open Mirage.Domain.Config
open Mirage.Domain.Setting
open Mirage.Domain.Null
open Mirage.Domain.Logger
open Mirage.Prelude

let [<Literal>] MinAudioDurationMs = 150
let [<Literal>] MinSilenceDurationMs = 2000
let [<Literal>] SamplesPerWindow = 2048
let [<Literal>] StartThreshold = 0.35f
let [<Literal>] EndThreshold = 0.2f

[<Struct>]
type ProcessingInput =
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
    {   forcedProbability: ValueOption<float32>
        allowRecordVoice: bool
    }

let mutable private isReady = false

/// A channel for pulling microphone data from __bufferChannel__ into a background thread to process.
let private processingChannel = Channel CancellationToken.None

[<Struct>]
type BufferInput =
    {   samples: Samples
        sampleRate: int
        channels: int
    }

/// A channel for pulling microphone data from the dissonance thread.
/// The given audio samples points to an array that belongs to the buffer pool, but we cannot hold this reference for long.
/// A deep copy of the audio samples is done, and then the input audio samples is returned to the buffer pool.
/// Audio data is then sent to __processingChannel__, where the audio samples are safe to use and will not be mutated.
let private bufferChannel =
    let channel = Channel CancellationToken.None
    let mutable i = 0
    let consumer () =
        forever <| fun () -> valueTask {
            let! input = readChannel channel
            if i < 10 then
                &i += 1
                logInfo $"{i} - bufferChannel consumer. StartOfRound is null: {isNull StartOfRound.Instance}"
            if isNotNull StartOfRound.Instance then
                writeChannel processingChannel << ValueSome <|
                    {   samples = input.samples
                        format =
                            {   sampleRate = input.sampleRate
                                channels = input.channels
                            }
                        isReady = isReady
                        isPlayerDead = StartOfRound.Instance.localPlayerController.isPlayerDead
                        pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
                        isMuted = getDissonance().IsMuted
                        allowRecordVoice = getSettings().allowRecordVoice
                    }
        }
    fork CancellationToken.None consumer
    channel

let mutable private micI = 0

type MicrophoneSubscriber() =
    interface IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            if micI < 10 then
                &micI += 1
                logInfo $"{micI} - MicrophoneSubscriber.ReceiveMicrophoneData. Frame count: {buffer.Count}"
            let samples = ArrayPool.Shared.Rent buffer.Count
            Buffer.BlockCopy(buffer.Array, buffer.Offset, samples, 0, buffer.Count * sizeof<float32>)
            writeChannel bufferChannel
                {   samples = { data = samples; length = buffer.Count }
                    sampleRate = format.SampleRate
                    channels = format.Channels
                }
        member _.Reset() =
            logInfo "MicrophoneSubscriber.Reset"
            writeChannel processingChannel ValueNone

let readMicrophone recordingDirectory =
    let silero = SileroVAD SamplesPerWindow
    let recorder =
        Recorder
            {   minAudioDurationMs = MinAudioDurationMs
                directory = recordingDirectory
                allowRecordVoice = _.allowRecordVoice
            }
    let voiceDetector =
        VoiceDetector
            {   minSilenceDurationMs = MinSilenceDurationMs
                forcedProbability = _.forcedProbability
                startThreshold = StartThreshold 
                endThreshold = EndThreshold
                detectSpeech = detectSpeech silero << _.data
                onVoiceDetected = fun state action -> writeRecorder recorder struct (state, action)
            }
    let resampler = Resampler SamplesPerWindow (writeDetector voiceDetector)
    let consumer () =
        forever <| fun () -> valueTask {
            let! value = readChannel processingChannel
            match value with
                | ValueNone -> writeResampler resampler ResamplerInput.Reset
                | ValueSome state ->
                    if state.isReady && isConfigReady() && (getConfig().enableRecordVoiceWhileDead || not state.isPlayerDead) then
                        let frame =
                            {   samples = state.samples
                                format = state.format
                            }
                        let processingState =
                            {   forcedProbability =
                                    if state.isMuted then ValueSome 0f
                                    else if state.pushToTalkEnabled then ValueSome 1f
                                    else ValueNone
                                allowRecordVoice = getSettings().allowRecordVoice
                            }
                        writeResampler resampler <| ResamplerInput struct (processingState, frame)
        }
    fork CancellationToken.None consumer

    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        logInfo "Prefix - DissonanceComms.Start"
        orig.Invoke self
        logInfo "Postfix - DissonanceComms.Start"
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber()
    )

    // Normally during the opening doors sequence, the game suffers from dropped audio frames, causing recordings to sound glitchy.
    // To reduce the likelihood of recording glitched sounds, audio recordings only start after the sequence is completely finished.
    On.StartOfRound.add_StartTrackingAllPlayerVoices(fun orig self ->
        logInfo "Prefix - StartOfRound.StartTrackingAllPlayerVoices"
        isReady <- true
        orig.Invoke self
        logInfo "Postfix - StartOfRound.StartTrackingAllPlayerVoices"
    )

    // In case the StartTrackingAllPlayerVoices hook fails due to another mod erroring.
    On.StartOfRound.add_OnShipLandedMiscEvents(fun orig self ->
        logInfo "Prefix - StartOfRound.OnShipLandedMiscEvents"
        isReady <- true
        orig.Invoke self
        logInfo "Postfix - StartOfRound.OnShipLandedMiscEvents"
    )

    // Set isReady: false when exiting to the main menu, or the round is over.
    On.StartOfRound.add_OnDestroy(fun orig self ->
        logInfo "Prefix - StartOfRound.OnDestroy"
        isReady <- false
        orig.Invoke self
        logInfo "Postfix - StartOfRound.OnDestroy"
    )

    On.StartOfRound.add_ReviveDeadPlayers(fun orig self ->
        logInfo "Prefix - StartOfRound.ReviveDeadPlayers"
        isReady <- false
        orig.Invoke self
        logInfo "Postfix - StartOfRound.ReviveDeadPlayers"
    )

    On.MenuManager.add_Awake(fun orig self ->
        logInfo "Prefix - MenuManager.Awake"
        orig.Invoke self
        logInfo "Postfix - MenuManager.Awake"
        writeChannel processingChannel ValueNone
    )