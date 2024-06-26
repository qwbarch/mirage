module Mirage.Unity.Predictor

#nowarn "40"

open System
open System.Collections.Generic
open GameNetcodeStuff
open FSharpx.Control
open Unity.Netcode
open Predictor.Domain
open Predictor.Lib
open Mirage.Core.Async.LVar
open Mirage.Unity.MimicPlayer

/// EntityId is a sum type. To serialize it for rpc methods, entity ids are converted to a string.
/// This enum helps clients know how to deserialize it once they receive the value.
type private IdType =
    | GuidType = 0
    | IntType = 1

let private toIdType = function
    | Guid _ -> IdType.GuidType
    | Int _ -> IdType.IntType

let private toEntityId (value: string) = function
    | IdType.GuidType -> Guid <| new Guid(value)
    | IdType.IntType -> Int <| uint64 value
    | idType -> invalidOp $"Invalid id type: {idType}"

/// Running ``ToString()`` on the entity id itself results in 
let private toIdString = function
    | Guid x -> x.ToString()
    | Int x -> x.ToString()

[<AllowNullLiteral>]
type Predictor() as self =
    inherit NetworkBehaviour()

    let mutable mimic: MimicPlayer = null
    let mutable player: PlayerControllerB = null

    let agent = new BlockingQueueAgent<GameInput>(Int32.MaxValue)
    let registerPredictor =
        if isNull mimic
            then userRegisterText
            else mimicRegisterText <| mimic.MimicId

    /// Predictor instance for the local player.
    static member val LocalPlayer = null with set, get

    /// Enemies with a predictor component.
    static member val Enemies = newLVar <| List<Predictor>()

    member _.SpeakerId
        with get() =
            if isNull player
                then Guid <| mimic.MimicId
                else Int player.playerSteamId

    member this.Awake() =
        mimic <- this.GetComponent<MimicPlayer>()
        player <- this.GetComponent<PlayerControllerB>()

    member this.Start() =
        if player = StartOfRound.Instance.localPlayerController then
            Predictor.LocalPlayer <- this

        // Since rpc methods can only be called on the unity thread,
        // game inputs are pulled into the consumer which executes actions on the unity thread.
        let rec consumer =
            async {
                let! gameInput = agent.AsyncGet()
                match gameInput with
                    | SpokeAtom payload -> registerPredictor <| SpokeAtom payload
                    | SpokeRecordingAtom payload -> registerPredictor <| SpokeRecordingAtom payload
                    | VoiceActivityAtom payload ->
                        let rpc =
                            if self.IsHost
                                then self.SyncVoiceActivityAtomClientRpc
                                else self.SyncVoiceActivityAtomServerRpc
                        rpc(
                            toIdString payload.speakerId,
                            toIdType payload.speakerId,
                            payload.prob
                        )
                    | HeardAtom payload ->
                        let rpc =
                            if self.IsHost
                                then self.SyncHeardAtomClientRpc
                                else self.SyncHeardAtomServerRpc
                        rpc(
                            payload.text,
                            toIdString payload.speakerClass,
                            toIdType payload.speakerClass,
                            toIdString payload.speakerId,
                            toIdType payload.speakerId,
                            payload.sentenceId.ToString(),
                            payload.elapsedMillis,
                            payload.transcriptionProb,
                            payload.nospeechProb
                        )
                do! consumer
            }
        Async.StartImmediate(consumer, this.destroyCancellationToken)

    override this.OnNetworkSpawn() =
        base.OnNetworkSpawn()
        Async.StartImmediate <<
            accessLVar Predictor.Enemies <| fun enemies ->
                enemies.Add this

    override this.OnNetworkDespawn() =
        base.OnNetworkDespawn()
        Async.StartImmediate <<
            accessLVar Predictor.Enemies <| fun enemies ->
                ignore <| enemies.Remove this

    /// Register an action with the predictor. This function is thread-safe.
    member _.Register(gameInput) = agent.Add gameInput

    [<ClientRpc>]
    member private _.SyncHeardAtomClientRpc(text, speakerClass, speakerClassType, speakerId, speakerIdType, sentenceId, elapsedTime, avgLogProb, noSpeechProb) =
        registerPredictor << HeardAtom <|
            {   text = text
                speakerClass = toEntityId speakerClass speakerClassType
                speakerId = toEntityId speakerId speakerIdType
                sentenceId = new Guid(sentenceId)
                elapsedMillis = elapsedTime
                transcriptionProb = avgLogProb
                nospeechProb = noSpeechProb
            }

    [<ServerRpc>]
    member private this.SyncHeardAtomServerRpc(text, speakerClass, speakerClassType, speakerId, speakerIdType, sentenceId, elapsedTime, avgLogProb, noSpeechProb) =
        if this.IsHost then
            this.SyncHeardAtomClientRpc(text, speakerClass, speakerClassType, speakerId, speakerIdType, sentenceId, elapsedTime, avgLogProb, noSpeechProb)

    [<ClientRpc>]
    member private _.SyncVoiceActivityAtomClientRpc(speakerId, speakerIdType, probability) =
        registerPredictor << VoiceActivityAtom <|
            {   speakerId = toEntityId speakerId speakerIdType
                prob = probability
            }

    [<ServerRpc>]
    member private this.SyncVoiceActivityAtomServerRpc(speakerId, speakerIdType, probability) =
        if this.IsHost then
            this.SyncVoiceActivityAtomClientRpc(
                speakerId,
                speakerIdType,
                probability
            )