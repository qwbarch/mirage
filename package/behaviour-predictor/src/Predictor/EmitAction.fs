module Predictor.EmitAction

open Domain
open System
open FSharpx.Control

let createActionEmitter
    (sendMimicText: Guid -> unit) = AutoCancelAgent<FutureAction>.Start(fun inbox ->
        let rec loop () =
            async {
                let! action = inbox.Receive()
                match action with
                | NoAction -> ()
                | QueueAction queueAction ->
                    let delay = queueAction.delay
                    let action = queueAction.action
                    do! Async.Sleep delay
                    sendMimicText action.fileId
                    Utilities.logInfo <| sprintf $"Emitting action with whisper: {queueAction.action.whisperTimings}"
                    Utilities.logInfo <| sprintf $"Emitting action with vad: {queueAction.action.vadTimings}"
                    do! Async.Sleep (action.duration + 10)

                    // Consume actions that accumulated
                    while inbox.CurrentQueueLength > 0 do
                        let! _ = inbox.TryReceive(1)
                        ()
                do! loop()
            }
        loop()
    )