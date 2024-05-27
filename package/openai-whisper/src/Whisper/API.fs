module Whisper.API

open System
open System.Diagnostics
open System.Text
open System.IO
open System.Reflection
open FSharpPlus
open Newtonsoft.Json
open Mirage.Core.Async.Lazy
open Mirage.Core.Async.Lock
open Mirage.Core.Async.Fork

/// Context required to interact with whisper.
type Whisper =
    private
        {   process': Process
            lock: Lock
        }

type WhisperException(message: string) =
    inherit Exception(message)

type TranscribeRequest =
    {   samplesBatch: byte[][]
        language: string
    }

type Transcription =
    {   text: string
        startTime: float32
        endTime: float32
        avgLogProb: float32
        noSpeechProb: float32
    }

type TranscribeResponse =
    {   response: Transcription[]
        error: string
    }

let w = new StreamWriter("whisper-log.txt", true)
let log (m: string) =
    w.WriteLine m
    w.Flush()

/// Start the whisper process. This should only be invoked once.
let startWhisper =
    Lazy.toAsync<Tuple<Whisper, bool>> <| fun () ->
        task {
            let baseDirectory =
                Assembly.GetExecutingAssembly().CodeBase
                    |> UriBuilder
                    |> _.Path
                    |> Uri.UnescapeDataString
                    |> Path.GetDirectoryName
            let workingDirectory = Path.Join(baseDirectory, "model/whisper-s2t")
            let process' = new Process()
            process'.StartInfo <-
                new ProcessStartInfo(
                    WorkingDirectory = workingDirectory,
                    FileName = Path.Join(workingDirectory, "main.exe"),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                )
            process'.StartInfo.StandardInputEncoding <- Encoding.UTF8
            process'.StartInfo.StandardOutputEncoding <- Encoding.UTF8

            // Close the child process if the parent dies.
            // From the python exe's side, it will also auto-close if the ZeroMQ socket is closed.
            AppDomain.CurrentDomain.ProcessExit.AddHandler(fun _ _ ->
                if not process'.HasExited then
                    process'.Kill()
            )

            // Start the child process.
            ignore <| process'.Start()
            let modelDirectory = Path.Join(baseDirectory, "model/whisper-base")
            log "sending model"
            do! process'.StandardInput.WriteLineAsync modelDirectory
            do! process'.StandardInput.FlushAsync()

            log "before readlineasync"
            let! response = process'.StandardOutput.ReadLineAsync()
            let mutable cudaAvailable = false
            ignore <| Boolean.TryParse(response, &cudaAvailable)
            let whisper =
                {   process' = process'
                    lock = createLock()
                }
            log $"return. cudaAvailable: {cudaAvailable}"
            return (whisper, cudaAvailable)
        }

/// Kill the whisper process.<br />
/// Note: The <b>CancellationToken</b> used during <b>startWhisper</b> is not implicitly cancelled or disposed.<br />
/// You must handle this yourself explicitly.
let stopWhisper whisper =
    try whisper.process'.Kill()
    finally dispose whisper.lock

/// Transcribe the given audio samples into text.
let transcribe whisper request =
    forkReturn << withLock' whisper.lock << Lazy.toAsync<Transcription[]> <| fun () ->
        task {
            log $"before send frame. samples: {request.samplesBatch[0].Length}"
            do! whisper.process'.StandardInput.WriteLineAsync(JsonConvert.SerializeObject request)
            log "after send frame"
            let! responseBody = whisper.process'.StandardOutput.ReadLineAsync()
            log $"response body: {responseBody}"
            let body = JsonConvert.DeserializeObject<TranscribeResponse> responseBody
            log $"body: {body}"
            return
                match Option.ofObj body.response, Option.ofObj body.error with
                    | Some response, None ->
                        flip map response <| fun transcription ->
                            //let probabilities =
                            //    softmax
                            //        [|  MathF.Exp transcription.avgLogProb
                            //            transcription.noSpeechProb
                            //        |]
                            {   transcription with
                                    avgLogProb = MathF.Exp transcription.avgLogProb //probabilities[0]
                            }
                    | None, Some error -> raise <| WhisperException error
                    | _, _ -> raise <| WhisperException $"Received an unexpected response from whisper-s2t. Response: {responseBody.ToString()}"
        }