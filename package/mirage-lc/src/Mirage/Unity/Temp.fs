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
open Mirage.Core.Async.LVar
open MimicVoice
open FSharpPlus
open Newtonsoft.Json
open Mirage.Core.Audio.File.Mp3Writer

// Temporarily hard-coding the local user's id.
let guid = Guid.NewGuid() // new Guid("37f6b68d-3ce2-4cde-9dc9-b6a68ccf002c")

type private SyncAction
    = SyncHeardAtom of Tuple<DateTime, string>
    | SyncVoiceActivityAtom of Tuple<Guid, DateTime>

[<AllowNullLiteral>]
type TranscriptionSyncer() as self =
    inherit NetworkBehaviour()

    let channel =
        let agent = new BlockingQueueAgent<SyncAction>(Int32.MaxValue)
        let rec consumer =
            async {
                let! action = agent.AsyncGet()
                match action with
                    | SyncHeardAtom (startTime, transcription) ->
                        let rpc =
                            if self.IsHost then self.SendTranscriptionClientRpc
                            else self.SendTranscriptionServerRpc
                        rpc(guid.ToString(), transcription, JsonConvert.SerializeObject startTime)
                        do! consumer
                    | SyncVoiceActivityAtom (speakerId, startTime) ->
                        let rpc =
                            if self.IsHost then self.VoiceActivityStartClientRpc
                            else self.VoiceActivityStartServerRpc
                        rpc(speakerId.ToString(), JsonConvert.SerializeObject startTime)
            }
        Async.StartImmediate consumer
        agent

    let sendTranscription (userId: string) text startTime =
        logInfo "received transcription"
        let userGuid = Guid <| new Guid(userId)
        let heardAtom =
            HeardAtom
                {   text = text
                    start = JsonConvert.DeserializeObject<DateTime> startTime
                    speakerClass = userGuid
                    speakerId = userGuid
                }
        Async.StartImmediate <| async {
            let! mimics = readLVar mimicsVar
            iter (flip mimicRegisterText heardAtom) mimics
        }
        if userGuid <> Guid guid then
            logInfo $"HeardAtom. Speaker: {guid}. Text: {text}"
            userRegisterText heardAtom

    let voiceActivityStrat (userId: string) startTime =
        logInfo $"voice activity started for user: {userId}"
        let userGuid = Guid <| new Guid(userId)
        let voiceActivityAtom =
            VoiceActivityAtom
                {   time = JsonConvert.DeserializeObject<DateTime> startTime
                    speakerId = userGuid
                }
        Async.StartImmediate <| async {
            let! mimics = readLVar mimicsVar
            iter (flip mimicRegisterText voiceActivityAtom) mimics
        }
    
    member _.SendTranscription(startTime, text) =
        logInfo "transcription syncer: sending transcription"
        channel.Add <| SyncHeardAtom(startTime, text)
    
    [<ClientRpc>]
    member this.SendTranscriptionClientRpc(userId, text, startTime) =
        if not this.IsHost then
            sendTranscription userId text startTime

    [<ServerRpc>]
    member this.SendTranscriptionServerRpc(userId, text, startTime) =
        if this.IsHost then
            this.SendTranscriptionClientRpc(userId, text, startTime)
        sendTranscription userId text startTime

    member _.VoiceActivityStart(speakerId, startTime) =
        logInfo "transcription syncer: sending voice activity start"
        channel.Add <| SyncVoiceActivityAtom(speakerId, startTime)

    [<ClientRpc>]
    member this.VoiceActivityStartClientRpc(speakerId, startTime) =
        if not this.IsHost then
            voiceActivityStrat speakerId startTime

    [<ServerRpc>]
    member this.VoiceActivityStartServerRpc(speakerId, startTime) =
        if this.IsHost then
            this.VoiceActivityStartClientRpc(speakerId, startTime)
        voiceActivityStrat speakerId startTime