module Whisper.Test.Inference

open NUnit.Framework
open Assertion
open FSharpPlus
open Whisper.API
open NAudio.Wave

let private printError<'A> (program: Result<'A, string>) : Unit =
    match program with
        | Ok _ -> ()
        | Error message -> printfn "%s" message

[<Test>]
let yo () =
    printError <| monad' {
        let whisper = startWhisper()
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