module App

open Silero.API
open Whisper.API
open NAudio.Wave
open System.Threading
open Mirage.Utilities.Audio.Speech
open FSharpPlus
open Mirage.Utilities.Audio.Format
open System
open Whisper.Transcribe

//[<EntryPoint>]
//let main _ =
//    Async.RunSynchronously <|
//        async {
//            let whisper = startWhisper (new CancellationTokenSource()).Token
//            let! cudaAvailable = isCudaAvailable whisper
//            printfn $"cuda available: {cudaAvailable}"
//            return!
//                initModel whisper
//                    {   useCuda = cudaAvailable
//                        cpuThreads = 4
//                        workers = 1
//                    }
//
//            let audioReader = new WaveFileReader("../jfk.wav")
//            let samples = Array.zeroCreate<byte> <| int audioReader.Length
//            ignore <| audioReader.Read(samples, 0, samples.Length)
//            let! transcription =
//                transcribe whisper
//                    {   samplesBatch = [ samples ]
//                        language = "en"
//                    }
//            printfn "%s" <| transcription[0].ToString()
//
//            stopWhisper whisper
//            return 0
//        }

[<EntryPoint>]
let main _ =
    Async.RunSynchronously <| async {
        let canceller = new CancellationTokenSource()

        printfn "Starting whisper."
        let whisper = startWhisper canceller.Token
        let! cudaAvailable = isCudaAvailable whisper
        printfn $"Cuda is enabled: {cudaAvailable}"
        return!
            initModel whisper
                {   //useCuda = cudaAvailable
                    useCuda = true
                    cpuThreads = 24
                    workers = 24
                }

        let transcriber = initTranscriber whisper

        let silero =
            initSilero
                {   cpuThreads = 4
                    workers = 1
                }
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
        let writeSamples = 
            initSpeechDetector
                {   detectSpeech = result << detectSpeech silero
                    onSpeechDetected = onSpeechDetected
                    canceller = new CancellationTokenSource()
                }
        let waveIn = new WaveInEvent()
        waveIn.WaveFormat <- new WaveFormat(16000, 16, 1)
        waveIn.BufferMilliseconds <- int <| 1024.0 / 16000.0 * 1000.0
        let onDataAvailable _ (event: WaveInEventArgs) =
            let samples = fromPCMBytes <| event.Buffer
            writeSamples samples
        waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable)
        waveIn.StartRecording()

        for x in 0 .. 100 do
            printfn ""
        ignore <| Console.ReadLine()
        releaseSilero silero
        stopWhisper whisper
        return 0
    }