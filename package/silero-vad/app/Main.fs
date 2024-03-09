module App

open NAudio.Wave
open System
open Silero.API

let toFloatPCM (pcmData: byte[]) =
    let bytesPerSample = 2
    let sampleCount = pcmData.Length / bytesPerSample
    Array.init sampleCount <| fun i ->
        let sampleValue = BitConverter.ToInt16(pcmData, i * bytesPerSample)
        float32 sampleValue / 32768.0f

let onDataAvailable silero _ (event: WaveInEventArgs) =
    let samples = toFloatPCM <| event.Buffer
    let x = detectSpeech silero samples
    printfn "probability: %f" x

[<EntryPoint>]
let main _ =
    let silero =
        initSilero
            {   cpuThreads = 4
                workers = 1
            }
    let waveIn = new WaveInEvent()
    waveIn.WaveFormat <- new WaveFormat(16000, 16, 1)
    waveIn.BufferMilliseconds <- int <| 1024.0 / 16000.0 * 1000.0
    waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable silero)
    waveIn.StartRecording()
    ignore <| Console.ReadLine()
    releaseSilero silero
    0