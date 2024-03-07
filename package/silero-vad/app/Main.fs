module App

open FSharpPlus
open System
open Silero.VAD
open System.Collections.Generic
open NAudio.Wave

let mutable audioBuffer: List<array<byte>> = new List<array<byte>>()

let convertBytesToFloat (byteArray: byte[]) =
    let bytesPerSample = 2 // Assuming 16-bit signed PCM samples
    let sampleCount = byteArray.Length / bytesPerSample

    let floatArray =
        Array.init sampleCount (fun i ->
            // Convert two bytes to a short (16-bit)
            let sampleValue = BitConverter.ToInt16(byteArray, i * bytesPerSample)

            // Normalize the sample value to the range [-1.0, 1.0]
            let normalizedValue = float32 sampleValue / 32768.0f

            // Store the normalized value in the float array
            normalizedValue
        )

    floatArray

let onDataAvailable silero _ (event: WaveInEventArgs) =
    audioBuffer.Add event.Buffer
    if audioBuffer.Count = 3 then
        let audioData = join <| Array.ofSeq audioBuffer
        let samples = convertBytesToFloat audioData
        audioBuffer.Clear()
        let x = detectSpeech silero samples
        printfn "%d" x.Length

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
    0