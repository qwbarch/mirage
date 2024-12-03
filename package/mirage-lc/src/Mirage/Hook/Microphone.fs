module Mirage.Hook.Microphone

#nowarn "40"

open Dissonance
open Dissonance.Audio.Capture
open System
open System.Collections.Concurrent
open System.Threading.Tasks
open System.Buffers
open System.Threading
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

let [<Literal>] SamplesPerWindow = 2048
let [<Literal>] StartThreshold = 0.5f
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
    {   forcedProbability: Option<float32>
        allowRecordVoice: bool
    }

let mutable private isReady = false

/// A channel for pulling microphone data from __bufferChannel__ into a background thread to process.
let private processingChannel = new BlockingQueueAgent<ValueOption<ProcessingInput>>(Int32.MaxValue)

[<Struct>]
type BufferInput =
    {   samples: Samples
        sampleRate: int
        sampleCount: int
        channels: int
    }

let private bufferPool: ArrayPool<float32> = ArrayPool.Create()

/// A channel for pulling microphone data from the dissonance thread.
/// The given audio samples points to an array that belongs to the buffer pool, but we cannot hold this reference for long.
/// A deep copy of the audio samples is done, and then the input audio samples is returned to the buffer pool.
/// Audio data is then sent to __processingChannel__, where the audio samples are safe to use and will not be mutated.
let private bufferChannel =
    let channel = ConcurrentQueue<BufferInput>()
    let rec consumer () =
        let mutable input = Unchecked.defaultof<BufferInput>
        while not <| channel.TryDequeue &input do
            Thread.Sleep 0
        let samples = Array.zeroCreate<float32> input.sampleCount
        Buffer.BlockCopy(input.samples, 0, samples, 0, input.sampleCount * sizeof<float32>)
        bufferPool.Return(input.samples, true)
        if not (isNull StartOfRound.Instance) then
            Async.StartImmediate << processingChannel.AsyncAdd << ValueSome <|
                {   samples = samples
                    format = WaveFormat(input.sampleRate, input.channels)
                    isReady = isReady
                    isPlayerDead = StartOfRound.Instance.localPlayerController.isPlayerDead
                    pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
                    isMuted = getDissonance().IsMuted
                    allowRecordVoice = getSettings().allowRecordVoice
                }
        consumer()
    ignore <| Task.Run consumer
    channel

type MicrophoneSubscriber() =
    interface IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            let samples = bufferPool.Rent buffer.Count
            Buffer.BlockCopy(buffer.Array, buffer.Offset, samples, 0, buffer.Count * sizeof<float32>)
            bufferChannel.Enqueue
                {   samples = samples
                    sampleRate = format.SampleRate
                    channels = format.Channels
                    sampleCount = buffer.Count
                }
        member _.Reset() = processingChannel.Add ValueNone

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
                startThreshold = StartThreshold 
                endThreshold = EndThreshold
                detectSpeech = result << detectSpeech silero
                onVoiceDetected = curry <| writeRecorder recorder
            }
    let resampler = Resampler SamplesPerWindow (writeDetector voiceDetector)
    let resample = Async.StartImmediate << writeResampler resampler
    let rec consumer =
        async {
            do! processingChannel.AsyncGet() |>> function
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

    On.MenuManager.add_Awake(fun orig self ->
        orig.Invoke self
        processingChannel.Add ValueNone
    )