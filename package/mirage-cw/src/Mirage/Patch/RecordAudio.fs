module Mirage.Patch.RecordAudio

#nowarn "40"

open System
open System.IO
open System.Threading
open Cysharp.Threading.Tasks
open FSharpPlus
open FSharpx.Control
open HarmonyLib
open Zorro.Recorder
open WebRtcVadSharp
open Photon.Voice.PUN
open NAudio.Wave
open Microsoft.FSharp.Core
open Mirage.Core.Audio.Format
open Mirage.Core.Audio.Resampler
open Mirage.Core.Field
open Mirage.Core.Audio.Recording

type MicrophoneAudio =
    private
        {   samples: float32[]
            sampleRate: int
            channels: int
            isRecording: bool
        }

let private get<'A> (field: Field<'A>) = field.Value

type RecordAudio() =
    static let vad = new WebRtcVad()
    static let mutable VoiceView: PhotonVoiceView = null

    static let audioChannel =
        let mutable vadDisabledFrames = 0
        let mutable framesWritten = 0
        let Recording = field()
        let FilePath = field()
        let channel = new BlockingQueueAgent<MicrophoneAudio>(Int32.MaxValue)
        let mutable resampler = None
        let rec consumer =
            async {
                let! audio = channel.AsyncGet()
                if Option.isNone resampler then
                    resampler <- Some <| defaultResampler audio.sampleRate 16000
                let resampledPcm = toPCMBytes <| resample resampler.Value audio.samples
                if vad.HasSpeech(resampledPcm, SampleRate.Is16kHz, FrameLength.Is20ms) then
                    vadDisabledFrames <- 0
                    framesWritten <- framesWritten + 1
                else
                    vadDisabledFrames <- vadDisabledFrames + 1

                if audio.isRecording && vadDisabledFrames <= 8 then // 160ms of audio
                    let defaultRecording () =
                        framesWritten <- 0
                        ignore <| Directory.CreateDirectory RecordingDirectory
                        let filePath = Path.Join(RecordingDirectory, $"{DateTime.UtcNow.ToFileTime()}.wav")
                        let recording = new WaveFileWriter(filePath, WaveFormat(audio.sampleRate, audio.channels))
                        set FilePath filePath
                        set Recording recording
                        recording
                    let recording = Option.defaultWith defaultRecording <| get Recording
                    return!
                        recording.WriteAsync (toPCMBytes audio.samples)
                            |> _.AsTask()
                            |> Async.AwaitTask
                else
                    ignore <| monad' {
                        let! recording = getValue Recording 
                        let! filePath = getValue FilePath
                        setNone Recording
                        setNone FilePath
                        dispose recording
                        if framesWritten <= 16 then
                            try File.Delete filePath
                            with | _ -> ()
                        vadDisabledFrames <- 0
                        framesWritten <- 0
                    }
                return! consumer
            }
        Async.Start consumer
        channel

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<PhotonVoiceView>, "Start")>]
    static member ``save photon voice view for later use``(__instance: PhotonVoiceView) =
        if not <| isNull __instance.RecorderInUse then
            VoiceView <-  __instance

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<RecorderAudioListener>, "SendMic")>]
    static member ``record audio to disk``(buffer, sampleRate, channels) =
        audioChannel.Add
            {   samples = buffer
                sampleRate = sampleRate
                channels = channels
                isRecording =
                    VoiceView.RecorderInUse.IsCurrentlyTransmitting // Only on when push-to-talk is enabled. Always on if voice activity is enabled.
                        && not (isNull Player.localPlayer)
                        && not Player.localPlayer.data.dead
            }