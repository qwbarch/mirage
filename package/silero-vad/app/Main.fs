module App

open FSharpPlus
open System
open System.Collections.Generic
open NAudio.Wave
open Silero.API

let mutable audioBuffer: List<array<byte>> = new List<array<byte>>()

let toFloatPCM (pcmData: byte[]) =
    let bytesPerSample = 2
    let sampleCount = pcmData.Length / bytesPerSample
    Array.init sampleCount (fun i ->
        let sampleValue = BitConverter.ToInt16(pcmData, i * bytesPerSample)
        float32 sampleValue / 32768.0f
    )

let onDataAvailable silero _ (event: WaveInEventArgs) =
    audioBuffer.Add event.Buffer
    if audioBuffer.Count >= 3 then
        let audioData = join <| Array.ofSeq audioBuffer
        let samples = toFloatPCM audioData
        audioBuffer.Clear()
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
    waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable silero)
    waveIn.StartRecording()
    ignore <| Console.ReadLine()
    releaseSilero silero
    0