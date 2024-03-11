module App

open NAudio.Wave
open System
open Silero.API
open Mirage.Utilities.Audio.Format
open Mirage.Utilities.Audio.Speech
open FSharpPlus
open System.Threading
open System.Collections.Generic

[<EntryPoint>]
let main _ =
    let silero =
        initSilero
            {   cpuThreads = 4
                workers = 1
            }
    let onSpeechDetected detection =
        match detection with
            | SpeechStart -> printfn "speech started"
            | SpeechEnd -> printfn "speech ended"
            | SpeechFound samples -> printfn $"sample count: {samples.Length}"
    let writeSamples = 
        initSpeechDetector
            {   detectSpeech = result << detectSpeech silero
                onSpeechDetected = result << onSpeechDetected
                canceller = new CancellationTokenSource()
            }
    let waveIn = new WaveInEvent()
    waveIn.WaveFormat <- new WaveFormat(16000, 16, 1)
    waveIn.BufferMilliseconds <- int <| 1024.0 / 16000.0 * 1000.0
    let onDataAvailable _ (event: WaveInEventArgs) =
        let samples = fromPCMBytes <| event.Buffer
        writeSamples samples
    waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable)
    waveIn.StartRecording()
    for x in 0 .. 100 do
        printfn ""
    ignore <| Console.ReadLine()
    releaseSilero silero
    0