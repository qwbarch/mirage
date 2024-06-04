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

// Temporarily hard-coding the local user's id.
let guid = Guid.NewGuid() // new Guid("37f6b68d-3ce2-4cde-9dc9-b6a68ccf002c")

type private SyncAction
    = SyncHeardAtom of Guid * string * Guid * int * float32 * float32
    | SyncVoiceActivityAtom of string

[<AllowNullLiteral>]
type TranscriptionSyncer() as self =
    inherit NetworkBehaviour()

    let sendTranscription sentenceId transcription userId elapsedTime avgLogProb noSpeechProb =
        logInfo "received transcription"
        let heardAtom =
            HeardAtom
                {   sentenceId = sentenceId
                    text = transcription
                    speakerClass = Guid userId
                    speakerId = Guid userId
                    elapsedMillis = elapsedTime
                    transcriptionProb = float avgLogProb
                    nospeechProb = float noSpeechProb
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

    let voiceActivityStart userId =
        logInfo $"voice activity started for user: {userId}"
        let voiceActivityAtom =
            VoiceActivityAtom
                {   speakerId = Guid userId
                    prob = 1.0
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
                    | SyncHeardAtom (sentenceId, transcription, userId, elapsedTime, avgLogProb, noSpeechProb) ->
                        let rpc =
                            if self.IsHost then self.SendTranscriptionClientRpc
                            else self.SendTranscriptionServerRpc
                    //let sendTranscription sentenceId transcription userId elapsedTime avgLogProb noSpeechProb =
                        rpc(sentenceId.ToString(), transcription, userId.ToString(), elapsedTime, avgLogProb, noSpeechProb)
                    | SyncVoiceActivityAtom userId ->
                        let rpc =
                            if self.IsHost then self.VoiceActivityStartClientRpc
                            else self.VoiceActivityStartServerRpc
                        rpc userId
                do! consumer
            }
        Async.StartImmediate consumer
        agent
    
    member _.SendTranscription(sentenceId, transcription, userId, elapsedTime, avgLogProb, noSpeechProb) =
        logInfo "transcription syncer: sending transcription"
        channel.Add <| SyncHeardAtom(sentenceId, transcription, userId, elapsedTime, avgLogProb, noSpeechProb)
    
    [<ClientRpc>]
    member _.SendTranscriptionClientRpc(sentenceId, transcription, userId, elapsedTime, avgLogProb, noSpeechProb) =
        sendTranscription (new Guid(sentenceId)) transcription (new Guid(userId)) elapsedTime avgLogProb noSpeechProb

    [<ServerRpc(RequireOwnership = false)>]
    member this.SendTranscriptionServerRpc(sentenceId, transcription, userId, elapsedTime, avgLogProb, noSpeechProb) =
        if this.IsHost then
            this.SendTranscriptionClientRpc(sentenceId, transcription, userId, elapsedTime, avgLogProb, noSpeechProb)

    member _.VoiceActivityStart(userId: Guid) =
        logInfo "transcription syncer: sending voice activity start"
        channel.Add << SyncVoiceActivityAtom <| userId.ToString()

    [<ClientRpc>]
    member _.VoiceActivityStartClientRpc(userId) =
        voiceActivityStart(new Guid(userId))

    [<ServerRpc(RequireOwnership = false)>]
    member this.VoiceActivityStartServerRpc(userId) =
        if this.IsHost then
            this.VoiceActivityStartClientRpc(userId)