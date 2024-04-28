module App

open FSharpPlus
open NAudio.Wave
open Silero.API
open System
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Speech

[<EntryPoint>]
let main _ =
    let silero = SileroVAD()
    let onSpeechDetected detection =
        match detection with
            | SpeechStart -> printfn "speech started"
            | SpeechEnd -> printfn "speech ended"
            | SpeechFound samples -> printfn $"sample count: {samples.Length}"
    let speechDetector =  SpeechDetector (result << detectSpeech silero) (result << onSpeechDetected)
    let waveIn = new WaveInEvent()
    waveIn.WaveFormat <- new WaveFormat(16000, 16, 1)
    waveIn.BufferMilliseconds <- int <| 1024.0 / 16000.0 * 1000.0
    let onDataAvailable _ (event: WaveInEventArgs) =
        let samples = fromPCMBytes <| event.Buffer
        Async.RunSynchronously <| writeSamples speechDetector samples
    waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable)
    waveIn.StartRecording()
    for _ in 0 .. 100 do
        printfn ""
    ignore <| Console.ReadLine()
    releaseSilero silero
    0