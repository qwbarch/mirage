module Whisper.API

open System
open System.IO
open System.Diagnostics
open System.Text
open System.Threading
open FSharp.Json
open FSharpPlus
open Mirage.Utilities.Async
open Mirage.Utilities.Lock

/// Context required to interact with whisper.
type Whisper =
    private
        {   process': Process
            cancelToken: CancellationToken
            lock: Lock
        }

type WhisperException(message: string) =
    inherit Exception(message)

// This cannot have a private constructor due to FSharpJson constraints.
type WhisperRequest<'A> =
    {   requestType: string   
        body: Option<'A>
    }

// This cannot have a private constructor due to FSharpJson constraints.
type WhisperResponse<'A> =
    {   response: Option<'A>
        error: Option<string>
    }

/// Start the whisper process. This should only be invoked once.
let startWhisper cancelToken =
    let whisper = new Process()
    whisper.StartInfo <-
        new ProcessStartInfo(
            FileName = "model/whisper-s2t/main.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        )
    whisper.StartInfo.StandardInputEncoding <- Encoding.UTF8
    whisper.StartInfo.StandardOutputEncoding <- Encoding.UTF8
    ignore <| whisper.Start()
    {   process' = whisper
        cancelToken = cancelToken
        lock = createLock()
    }

/// Kill the whisper process.<br />
/// Note: The <b>CancellationToken</b> used during <b>startWhisper</b> is not implicitly cancelled or disposed.<br />
/// You must handle this yourself explicitly.
let stopWhisper whisper =
    try whisper.process'.Kill()
    finally dispose whisper.lock

let private request<'A, 'B> whisper (request: WhisperRequest<'A>) : Async<'B> =
    withLock' whisper.lock << Lazy.toAsync<'B> <| fun () ->
        task {
            do! whisper.process'.StandardInput.WriteAsync(Json.serializeU request)
            do! whisper.process'.StandardInput.WriteAsync '\x00'
            do! whisper.process'.StandardInput.FlushAsync()
            let response = new StringBuilder()
            let mutable running = true
            while running do
                let buffer = new Memory<char>(Array.zeroCreate<char> 1)
                let! bytesRead = whisper.process'.StandardOutput.ReadAsync(buffer, whisper.cancelToken)
                if bytesRead = 0 then
                    raise <| WhisperException "Unexpectedly read 0 bytes from whisper process' stdout."
                let letter = buffer.Span[0]
                if letter = '\x00' then
                    running <- false
                else
                    ignore <| response.Append letter
            let body = Json.deserialize <| response.ToString()
            return
                match body.response, body.error with
                    | Some response, None -> response
                    | None, Some error -> raise <| WhisperException error
                    | _, _ -> raise <| WhisperException $"Received an unexpected response from whisper-s2t. Response: {response.ToString()}"
        }

let isCudaAvailable whisper =
    request<string, bool> whisper
        {   requestType = "isCudaAvailable" 
            body = None
        }

type InitModelParams =
    {   useCuda: bool
        cpuThreads: int 
        workers: int
    }

type InitModelFullParams =
    {   modelPath: string
        useCuda: bool
        cpuThreads: int 
        workers: int
    }

/// Initialize the whisper model. This needs to be run at least once for <b>transcribe</b> to work.
let initModel whisper (modelParams: InitModelParams) =
    let baseDirectory = AppDomain.CurrentDomain.BaseDirectory
    let fullParams =
        {   modelPath = Path.Join(baseDirectory, "model/whisper-base")
            useCuda = modelParams.useCuda
            cpuThreads = modelParams.cpuThreads
            workers = modelParams.workers
        }
    map ignore <| request<InitModelFullParams, string> whisper
        {   requestType = "initModel"
            body = Some fullParams
        }

/// A batch of samples to transcribe.
type TranscribeParams =
    {   samplesBatch: list<array<byte>>
        language: string
    }

/// A transcription of the given audio samples.
type Transcription =
    {   text: string
        startTime: float32
        endTime: float32
        avgLogProb: float32
        noSpeechProb: float32
    }

/// Transcribe the given audio samples into text.
let transcribe whisper transcribeParams =
    request<TranscribeParams, array<Transcription>> whisper
        {   requestType = "transcribe"
            body = Some transcribeParams
        }