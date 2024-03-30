module Whisper.Test.Inference

open System.Threading
open Assertion
open NAudio.Wave
open NUnit.Framework
open Whisper.API
open System.Diagnostics

let f whisper =
    async {
        let audioReader = new WaveFileReader("jfk.wav")
        let samples = Array.zeroCreate<byte> <| int audioReader.Length
        ignore <| audioReader.Read(samples, 0, samples.Length)
        let sw = Stopwatch.StartNew()
        let! transcription =
            transcribe whisper
                {   samplesBatch = List.replicate 10 samples
                    language = "en"
                }
        sw.Stop()
        printfn $"Elapsed time: {sw.ElapsedMilliseconds} seconds"
        let expected = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."
        let actual = transcription[0].text
        assertEquals expected actual
    }

[<Test>]
let ``test transcription on jfk.wav`` () =
    async {
        let whisper = startWhisper (new CancellationTokenSource()).Token
        return!
            initModel whisper
                {   //useCuda = false
                    useCuda = true
                    cpuThreads = 24
                    workers = 24
                }
        for i in 0 .. 10 do
            return! f whisper
        stopWhisper whisper
    }