module Mirage.Unity.Temp

#nowarn "40"

open System
open Unity.Netcode
open Predictor.Lib
open Predictor.Domain
open Predictor.Utilities
open FSharpx.Control
open Mirage.Core.Async.LVar
open MimicVoice
open FSharpPlus
open Newtonsoft.Json

// Temporarily hard-coding the local user's id.
let guid = Guid.NewGuid() // new Guid("37f6b68d-3ce2-4cde-9dc9-b6a68ccf002c")

type private SyncAction
    = SyncHeardAtom of Tuple<Guid, DateTime, string>
    | SyncVoiceActivityAtom of Tuple<string, string>

[<AllowNullLiteral>]
type TranscriptionSyncer() as self =
    inherit NetworkBehaviour()

    let sendTranscription (userId: Guid) (startTime: DateTime) (transcription: string) =
        logInfo "received transcription"
        let heardAtom =
            HeardAtom
                {   text = transcription
                    start = startTime
                    speakerClass = Guid userId
                    speakerId = Guid userId
                }
        Async.StartImmediate <| async {
            let! mimics = readLVar mimicsVar
            flip iter mimics <| fun (mimickingPlayer, mimicId) ->
                if StartOfRound.Instance.localPlayerController = mimickingPlayer then
                    logInfo $"HeardAtom received for mimic: {mimicId}"
                    mimicRegisterText mimicId heardAtom
                else
                    logInfo "HeardAtom ignored due to mimic belonging to a different player."
        }
        if userId <> guid then
            logInfo $"HeardAtom. Speaker: {guid}. Text: {transcription}"
            userRegisterText heardAtom

    let voiceActivityStrat userId startTime =
        logInfo $"voice activity started for user: {userId}"
        let voiceActivityAtom =
            VoiceActivityAtom
                {   time = startTime
                    speakerId = Guid userId
                }
        Async.StartImmediate <| async {
            let! mimics = readLVar mimicsVar
            flip iter mimics <| fun (mimickingPlayer, mimicId) ->
                if StartOfRound.Instance.localPlayerController = mimickingPlayer then
                    logInfo $"VoiceActivityAtom received for mimic: {mimicId}."
                    mimicRegisterText mimicId voiceActivityAtom
                else
                    logInfo "VoiceActivityAtom ignored due to mimic belonging to a different player."
        }
        userRegisterText voiceActivityAtom

    let channel =
        let agent = new BlockingQueueAgent<SyncAction>(Int32.MaxValue)
        let rec consumer =
            async {
                let! action = agent.AsyncGet()
                match action with
                    | SyncHeardAtom (userId, startTime, transcription) ->
                        let rpc =
                            if self.IsHost then self.SendTranscriptionClientRpc
                            else self.SendTranscriptionServerRpc
                        rpc(userId.ToString(), startTime.ToString(), transcription)
                    | SyncVoiceActivityAtom (userId, startTime) ->
                        let rpc =
                            if self.IsHost then self.VoiceActivityStartClientRpc
                            else self.VoiceActivityStartServerRpc
                        rpc(userId, startTime)
                do! consumer
            }
        Async.StartImmediate consumer
        agent
    
    member _.SendTranscription(startTime: DateTime, text: string) =
        logInfo "transcription syncer: sending transcription"
        channel.Add <| SyncHeardAtom(guid, startTime, text)
    
    [<ClientRpc>]
    member _.SendTranscriptionClientRpc(userId: string, startTime, text) =
        sendTranscription (new Guid(userId)) (DateTime.Parse startTime) text

    [<ServerRpc(RequireOwnership = false)>]
    member this.SendTranscriptionServerRpc(userId, startTime, text) =
        if this.IsHost then
            this.SendTranscriptionClientRpc(userId, startTime, text)

    member _.VoiceActivityStart(userId: Guid, startTime: DateTime) =
        logInfo "transcription syncer: sending voice activity start"
        channel.Add <| SyncVoiceActivityAtom(userId.ToString(), startTime.ToString())

    [<ClientRpc>]
    member _.VoiceActivityStartClientRpc(userId, startTime) =
        voiceActivityStrat (new Guid(userId)) (DateTime.Parse startTime)

    [<ServerRpc(RequireOwnership = false)>]
    member this.VoiceActivityStartServerRpc(userId, startTime) =
        if this.IsHost then
            this.VoiceActivityStartClientRpc(userId, startTime)