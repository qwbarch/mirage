module Mirage.Hook.Microphone

#nowarn "40"

open Dissonance.Audio.Capture
open System
open FSharpPlus
open FSharpx.Control
open NAudio.Wave
open Dissonance
open Silero.API
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Recorder
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
    }

let private channel = new BlockingQueueAgent<RecordState>(Int32.MaxValue)
let mutable private isReady = false
let mutable private dissonance: DissonanceComms = null

type MicrophoneSubscriber() =
    interface IMicrophoneSubscriber with
        member this.ReceiveMicrophoneData(buffer, format) =
            if not <| isNull StartOfRound.Instance then
                Async.StartImmediate <<
                    channel.AsyncAdd <|
                        {   samples = buffer.ToArray()
                            format= WaveFormat(format.SampleRate, format.Channels)
                            isReady = isReady
                            isPlayerDead = StartOfRound.Instance.localPlayerController.isPlayerDead
                            pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
                            isMuted = not dissonance.IsMuted
                        }
        member _.Reset() = ()

let readMicrophone recordingDirectory =
    let subscriber = MicrophoneSubscriber()
    let silero = SileroVAD SamplesPerWindow
    let recorder = Recorder recordingDirectory << konst <| result ()
    let voiceDetector = VoiceDetector (result << detectSpeech silero) (writeRecorder recorder)
    let resampler = Resampler (writeDetector voiceDetector)
    let rec consumer =
        async {
            let! state = channel.AsyncGet()
            let recordWhileDead = true //getSettings().recordWhileDead || state.isPlayerDead
            let pushToTalkPressed = state.pushToTalkEnabled && not state.isMuted
            if state.isReady && recordWhileDead then
                Async.StartImmediate <|
                    writeResampler resampler
                        {   samples = state.samples
                            format = state.format
                        }
            do! consumer
        }
    Async.Start consumer

    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        dissonance <- self
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber()
    )

    // Normally during the opening doors sequence, the game suffers from dropped audio frames, causing recordings to sound glitchy.
    // To reduce the likelihood of recording glitched sounds, audio recordings only start after the sequence is completely finished.
    On.StartOfRound.add_openingDoorsSequence(fun orig self ->
        isReady <- true
        orig.Invoke self
    )

    On.StartOfRound.add_OnDestroy(fun orig self ->
        isReady <- false
        orig.Invoke self
    )