module Whisper.Test.Inference

open System.Diagnostics
open Assertion
open NAudio.Wave
open NUnit.Framework
open Whisper.API

let runTest whisper =
    async {
        let audioReader = new WaveFileReader("jfk.wav")
        let samples = Array.zeroCreate<byte> <| int audioReader.Length
        ignore <| audioReader.Read(samples, 0, samples.Length)
        let sw = Stopwatch.StartNew()
        let! transcription =
            transcribe whisper
                {   
                    samplesBatch = List.replicate 10 samples
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
        let whisper = Whisper()
        let! useCuda = isCudaAvailable whisper
        printfn $"useCuda: {useCuda}"
        do!
            initModel whisper
                {   useCuda = useCuda
                    cpuThreads = 16
                    workers = 16
                }
        for _ in 0 .. 10 do
            do! runTest whisper
        stopWhisper whisper
    }