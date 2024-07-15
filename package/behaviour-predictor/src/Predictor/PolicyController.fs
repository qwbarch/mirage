module Predictor.PolicyController

open Domain
open FSharpx.Control
open Mirage.Core.Async.LVar
open System.Collections.Generic
open System
open Utilities

// Updates the policy for each individal mimic
let createPolicyUpdater 
    (internalPolicyLVar: LVar<Policy>)
    (internalRecordingsLVar: LVar<HashSet<Guid>>) =
    AutoCancelAgent<PolicyUpdateMessage>.Start(fun inbox ->
        let rec loop () =
            async {
                let! policyUpdateMessage = inbox.Receive()
                match policyUpdateMessage with
                | ObsActionPair (obsTime, obs, action) -> 
                    let! _ = accessLVar internalPolicyLVar <| fun internalPolicy ->
                        internalPolicy[obsTime] <- (obs, action)
                    match action with
                    | NoAction -> ()
                    | QueueAction queueAction -> 
                        let! _ = accessLVar internalRecordingsLVar <| fun internalRecordings ->
                            ignore <| internalRecordings.Add(queueAction.action.fileId)
                            logInfo <| sprintf $"Successfully added recording guid {queueAction.action.fileId}"
                        ()
                | RemoveRecording fileId ->
                    let! _ = accessLVar internalRecordingsLVar <| fun internalRecordings ->
                        ignore <| internalRecordings.Remove(fileId)
                        logInfo <| sprintf $"Successfully erased a recording from the mimic set."
                    ()
                do! loop()
            }
        loop()
    )

let createPolicyDeleter
    (internalPolicyLVar: LVar<Policy>) = AutoCancelAgent<PolicyDeleteMessage>.Start(fun inbox ->
    let rec loop () =
        async {
            let! policyDeleteMessage = inbox.Receive()
            match policyDeleteMessage with
            | RemovePolicy rem ->
                let! sizePostRemoval = accessLVar internalPolicyLVar <| fun internalPolicy ->
                    for obsTime, _, _ in rem do
                        ignore <| internalPolicy.Remove(obsTime)
                    internalPolicy.Count
                logInfo <| sprintf $"Mimic got policy removal {rem.Length}. Size after: {sizePostRemoval}"
            do! loop()
        }
    loop()
    )