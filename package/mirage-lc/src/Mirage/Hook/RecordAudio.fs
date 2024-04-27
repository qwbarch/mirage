module Mirage.Hook.RecordAudio

#nowarn "40"

open NAudio.Wave
open System
open System.Collections.Generic
open Silero.API
open FSharpPlus
open FSharpx.Control
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.Resampler
open Mirage.Domain.Logger

let [<Literal>] private SampleRate = 16000
let private resampler = Resampler()
let private silero = SileroVAD()
let private speechDetector =
    initSpeechDetector (result << detectSpeech silero) <| fun detection ->
        match detection with
            | SpeechStart -> result <| logInfo "speech started"
            | SpeechFound _ -> result ()
            | SpeechEnd -> result <| logInfo "speech ended"

type AudioFrame =
    private
        {   samples: float32[]
            format: WaveFormat
        }

type private MicrophoneSubscriber() =
    let audioBuffer = new List<float32[]>()
    let channel =
        let agent = new BlockingQueueAgent<AudioFrame>(Int32.MaxValue)
        let rec consumer =
            async {
                let! frame = agent.AsyncGet()
                let samples =
                    if frame.format.SampleRate <> SampleRate then
                        setRates resampler frame.format.SampleRate SampleRate
                        resample resampler frame.samples
                    else
                        frame.samples
                do! writeSamples speechDetector samples
                do! consumer
            }
        Async.Start consumer // TODO: fix this later
        agent

    interface Dissonance.Audio.Capture.IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) =
            // Dissonance provides 10ms audio frames (160 samples), but SileroVAD expects 30ms audio frames (480 samples).
            audioBuffer.Add buffer.Array
            if audioBuffer.Count >= 3 then
                channel.Add { samples = join <| Array.ofSeq audioBuffer; format = format }
                audioBuffer.Clear()
        member _.Reset() =
            audioBuffer.Clear()
            // TODO: Send SpeechEnd to speech detector, but also have it handle the case where the file is already closed.

let recordAudio () =
    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        self.SubscribeToRecordedAudio <| MicrophoneSubscriber()
    )