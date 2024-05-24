module Mirage.Unity.Temp

#nowarn "40"

open System
open Unity.Netcode
open UnityEngine
open Predictor.Lib
open Predictor.Domain
open Predictor.Utilities
open FSharpx.Control
open Predictor.MimicPool
open AudioStream

// Temporarily hard-coding the local user's id.
let guid = Guid.NewGuid() // new Guid("37f6b68d-3ce2-4cde-9dc9-b6a68ccf002c")

[<AllowNullLiteral>]
type TranscriptionSyncer() as self =
    inherit NetworkBehaviour()

    let channel =
        let agent = new BlockingQueueAgent<string>(Int32.MaxValue)
        let rec consumer =
            async {
                let! transcription = agent.AsyncGet()
                let rpc =
                    if self.IsHost then self.SendTranscriptionClientRpc
                    else self.SendTranscriptionServerRpc
                rpc(guid.ToString(), transcription)
                do! consumer
            }
        Async.StartImmediate consumer
        agent

    let sendTranscription (userId: string) text =
        logInfo "received transcription"
        let gguid = Guid <| new Guid(userId)
        if gguid <> Guid guid then
            logInfo $"HeardAtom. Speaker: {guid}. Text: {text}"
            userRegisterText <| HeardAtom
                {   text = text
                    start = DateTime.UtcNow
                    speakerClass = Guid guid
                    speakerId = Guid guid
                }
    
    member _.SendTranscription(text) =
        logInfo "transcription syncer: sending transcription"
        channel.Add text
    
    [<ClientRpc>]
    member this.SendTranscriptionClientRpc(userId, text) =
        if not this.IsHost then
            sendTranscription userId text

    [<ServerRpc>]
    member this.SendTranscriptionServerRpc(userId, text) =
        if this.IsHost then
            this.SendTranscriptionClientRpc(userId, text)
        sendTranscription userId text