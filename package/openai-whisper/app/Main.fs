module App

open System
open System.Diagnostics
open Silero.API
open Whisper.API
open NAudio.Wave
open FSharpPlus
open Mirage.Core.Async.LVar
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.PCM

let [<Literal>] WindowSize = 1024

type Transcriber =
    private
        {   whisper: Whisper
            samples: LVar<byte[]>
            readSamples: LVar<int>
        }

let initTranscriber whisper =
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
                    let! transcription =  transcribe whisper { samplesBatch = [|samples|]; language = "en" }
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

[<EntryPoint>]
let main _ =
    Async.RunSynchronously <| async {
        printfn "Starting whisper."
        let! (whisper, cudaAvailable) = startWhisper
        printfn $"Cuda is enabled: {cudaAvailable}"
        let transcriber = initTranscriber whisper
        let silero = SileroVAD WindowSize
        let onSpeechDetected detection =
            async {
                match detection with
                    | SpeechStart -> printfn "Speech started."
                    | SpeechEnd ->
                        printfn "Speech ended."
                        return! addAudioFrame transcriber None
                    | SpeechFound samples ->
                        return! addAudioFrame transcriber << Some <| toPCMBytes samples
            }
        let speechDetector =
            SpeechDetector
                (result << detectSpeech silero)
                onSpeechDetected
        let waveIn = new WaveInEvent()
        waveIn.WaveFormat <- new WaveFormat(16000, 16, 1)
        waveIn.BufferMilliseconds <- int <| float WindowSize / 16000.0 * 1000.0
        let onDataAvailable _ (event: WaveInEventArgs) =
            let samples = fromPCMBytes <| event.Buffer
            Async.RunSynchronously <| writeSamples speechDetector samples
        waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable)
        waveIn.StartRecording()

        for _ in 0 .. 100 do
            printfn ""
        ignore <| Console.ReadLine()
        releaseSilero silero
        stopWhisper whisper
        return 0
    }