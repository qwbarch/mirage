module Whisper.Test.Inference

open System.Threading
open Assertion
open NAudio.Wave
open NUnit.Framework
open Whisper.API

[<Test>]
let ``test transcription on jfk.wav`` () =
    async {
        let whisper = startWhisper (new CancellationTokenSource()).Token
        return!
            initModel whisper
                {   useCuda = false
                    cpuThreads = 1
                    workers = 1
                }
        let audioReader = new WaveFileReader("jfk.wav")
        let samples = Array.zeroCreate<byte> <| int audioReader.Length
        ignore <| audioReader.Read(samples, 0, samples.Length)
        let! transcription =
            transcribe whisper
                {   samplesBatch = [ samples ]
                    language = "en"
                }
        let expected = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."
        let actual = transcription[0].text
        assertEquals expected actual
        stopWhisper whisper
    }