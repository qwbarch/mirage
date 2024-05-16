module Whisper.API

open System
open System.Diagnostics
open System.Text
open SolTechnology.Avro
open FSharpPlus
open FSharp.Json
open NetMQ
open NetMQ.Sockets
open Mirage.Core.Async.Lazy
open Mirage.Core.Async.Lock
open Mirage.Core.Async.Fork

/// Context required to interact with whisper.
type Whisper =
    private
        {   process': Process
            lock: Lock
            socket: PushSocket
            schema: string
        }

type WhisperException(message: string) =
    inherit Exception(message)

type TranscribeRequest =
    {   samplesBatch: array<array<byte>>
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
    {   response: Option<Transcription[]>
        error: Option<string>
    }

/// Start the whisper process. This should only be invoked once.
let startWhisper =
    Lazy.toAsync<Tuple<Whisper, bool>> <| fun () ->
        task {
            let process' = new Process()
            process'.StartInfo <-
                new ProcessStartInfo(
                    FileName = "model/whisper-s2t/main.exe",
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

            // Start the child process, and retrieve whether cuda is available or not.
            ignore <| process'.Start()
            let schema = AvroConvert.GenerateSchema typeof<TranscribeRequest>
            do! process'.StandardInput.WriteLineAsync schema
            let! response = process'.StandardOutput.ReadLineAsync()
            let mutable cudaAvailable = false
            ignore <| Boolean.TryParse(response, &cudaAvailable)
            let whisper =
                {   process' = process'
                    lock = createLock()
                    socket = new PushSocket("@tcp://localhost:50292")
                    schema = schema
                }
            return (whisper, cudaAvailable)
        }

/// Kill the whisper process.<br />
/// Note: The <b>CancellationToken</b> used during <b>startWhisper</b> is not implicitly cancelled or disposed.<br />
/// You must handle this yourself explicitly.
let stopWhisper whisper =
    try
        whisper.process'.Kill()
    finally
        dispose whisper.lock
        dispose whisper.socket

/// Transcribe the given audio samples into text.
let transcribe whisper request =
    forkReturn << withLock' whisper.lock << Lazy.toAsync<Transcription[]> <| fun () ->
        task {
            whisper.socket.SendFrame(AvroConvert.SerializeHeadless(request, whisper.schema))
            let! responseBody = whisper.process'.StandardOutput.ReadLineAsync()
            let body = Json.deserialize responseBody
            return
                match body.response, body.error with
                    | Some response, None -> response
                    | None, Some error -> raise <| WhisperException error
                    | _, _ -> raise <| WhisperException $"Received an unexpected response from whisper-s2t. Response: {responseBody.ToString()}"
        }