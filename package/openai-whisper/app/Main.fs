module App

open Whisper.API
open NAudio.Wave
open System.Threading

[<EntryPoint>]
let main _ =
    Async.RunSynchronously <|
        async {
            let whisper = startWhisper (new CancellationTokenSource()).Token
            let! cudaAvailable = isCudaAvailable whisper
            printfn $"cuda available: {cudaAvailable}"
            return!
                initModel whisper
                    {   useCuda = cudaAvailable
                        cpuThreads = 4
                        workers = 1
                    }

            let audioReader = new WaveFileReader("../jfk.wav")
            let samples = Array.zeroCreate<byte> <| int audioReader.Length
            ignore <| audioReader.Read(samples, 0, samples.Length)
            let! transcription =
                transcribe whisper
                    {   samplesBatch = [ samples ]
                        language = "en"
                    }
            printfn "%s" <| transcription[0].ToString()

            stopWhisper whisper
            return 0
        }