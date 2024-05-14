module Whisper.Transcribe

open Whisper.API
open Mirage.Core.Async.LVar
open FSharpPlus
open System.Diagnostics

/// A live transcriber.
type Transcriber =
    private
        {   whisper: Whisper
            samples: LVar<byte[]>
            readSamples: LVar<int>
        }

let Transcriber whisper =
    let samplesVar = newLVar [||]
    let readSamplesVar = newLVar 0
    Async.Start <|
        async {
            while true do
                let! samples = readLVar samplesVar
                let! readSamples = readLVar readSamplesVar
                if samples.Length = 0 || samples.Length = readSamples then
                    return! Async.Sleep 100
                else
                    return! modifyLVar readSamplesVar (konst samples.Length)
                    let sw = Stopwatch.StartNew()
                    let! transcription =  transcribe whisper { samplesBatch = [samples]; language = "en" }
                    sw.Stop()
                    printfn $"Elapsed time: {float sw.ElapsedMilliseconds / 1000.0} seconds"
                    printfn $"Length: {samples.Length}"
                    printfn $"Transcription: {transcription[0]}"
        }
    {   whisper = whisper
        samples = samplesVar
        readSamples = readSamplesVar
    }

let addAudioFrame transcriber frame =
    match frame with
        | None ->
            modifyLVar transcriber.samples (konst zero)
                *> modifyLVar transcriber.readSamples (konst zero)
        | Some samples -> modifyLVar transcriber.samples (flip Array.append samples)