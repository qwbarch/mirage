module Whisper.Test.Inference

open System.Diagnostics
open Assertion
open NAudio.Wave
open NUnit.Framework
open Whisper.API

let runTest whisper samples =
    async {
        let sw = Stopwatch.StartNew()
        let! transcription =
            transcribe whisper
                {   
                    samplesBatch = List.replicate 10 samples
                    language = "en"
                }
        sw.Stop()
        printfn $"Elapsed time: {sw.Elapsed.TotalSeconds} seconds"
        let expected = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."
        let actual = transcription[0].text
        assertEquals expected actual
    }

[<Test>]
let ``test transcription on jfk.wav`` () =
    async {
        let whisper = Whisper()
        let audioReader = new WaveFileReader("jfk.wav")
        let samples = Array.zeroCreate<byte> <| int audioReader.Length
        ignore <| audioReader.Read(samples, 0, samples.Length)
        let! useCuda = isCudaAvailable whisper
        printfn $"useCuda: {useCuda}"
        do!
            initModel whisper
                {   useCuda = useCuda
                    cpuThreads = 4
                    workers = 1
                }
        for _ in 0 .. 10 do
            do! runTest whisper samples
        stopWhisper whisper
    }