module Predictor.PolicyController

open Domain
open FSharpx.Control
open Mirage.Core.Async.LVar

// Updates the policy for each individal mimic
let createPolicyUpdater 
    (internalPolicyLVar: LVar<Policy>) =
    AutoCancelAgent<PolicyUpdateMessage>.Start(fun inbox ->
        let rec loop () =
            async {
                let! (obsTime, obs, action) = inbox.Receive()
                let! _ = accessLVar internalPolicyLVar <| fun internalPolicy ->
                    internalPolicy[obsTime] <- (obs, action)
                do! loop()
            }
        loop()
    )