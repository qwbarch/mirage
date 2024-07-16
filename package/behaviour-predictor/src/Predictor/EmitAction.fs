module Predictor.EmitAction

open Domain
open System
open FSharpx.Control

let createActionEmitter
    (sendMimicText: MimicMessage -> unit) = AutoCancelAgent<FutureAction>.Start(fun inbox ->
        let rec loop () =
            async {
                let! action = inbox.Receive()
                match action with
                | NoAction -> ()
                | QueueAction queueAction ->
                    let delay = queueAction.delay
                    let action = queueAction.action
                    do! Async.Sleep delay

                    let mimicMessage: MimicMessage =
                        {   recordingId = action.fileId
                            whisperTimings = action.whisperTimings
                            vadTimings = action.vadTimings
                        }
                    sendMimicText mimicMessage
                    Utilities.logInfo <| sprintf $"Emitting mimic message: {mimicMessage}"
                    do! Async.Sleep (action.duration + 10)

                    // Consume actions that accumulated
                    while inbox.CurrentQueueLength > 0 do
                        let! _ = inbox.TryReceive(1)
                        ()
                do! loop()
            }
        loop()
    )