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
open Mirage.Domain.Logger

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

    /// Whether or not a payload received via a client rpc should be executed.
    let shouldRegister () =
        not (isNull player) || not (isNull mimic) && mimic.MimickingPlayer = StartOfRound.Instance.localPlayerController

    let agent = new BlockingQueueAgent<GameInput>(Int32.MaxValue)
    let registerPredictor gameInput =
        if isNull mimic
            then
                logInfo "mimic is null: predictor will run userRegisterText"
                userRegisterText gameInput
            else
                logInfo "mimic is not null: predictor will run mimicRegisterText"
                mimicRegisterText mimic.MimicId gameInput

    /// Predictor instance for the local player.
    static member val LocalPlayer: Predictor = null with set, get

    /// Enemies with a predictor component.
    static member val Enemies = newLVar <| List<Predictor>()

    /// EntityId of the speaker. If this predictor belongs to a non-local player, this will throw an error.
    member _.SpeakerId
        with get() =
            if isNull player then Guid <| mimic.MimicId
            else if player = StartOfRound.Instance.localPlayerController then
                Int player.playerSteamId
            else
                invalidOp "Cannot retrieve the SpeakerId of the non-local player."

    member this.Awake() =
        mimic <- this.GetComponent<MimicPlayer>()
        player <- this.GetComponent<PlayerControllerB>()

    member this.Start() =
        // Since rpc methods can only be called on the unity thread,
        // game inputs are pulled into the consumer which executes actions on the unity thread.
        let rec consumer =
            async {
                let! gameInput = agent.AsyncGet()
                logInfo "Predictor component received game input"
                match gameInput with
                    | SpokeAtom payload ->
                        logInfo "Predictor received SpokeAtom payload"
                        registerPredictor <| SpokeAtom payload
                    | SpokeRecordingAtom payload ->
                        logInfo "Predictor received SpokeRecordingAtom payload"
                        registerPredictor <| SpokeRecordingAtom payload
                    | VoiceActivityAtom payload ->
                        logInfo "before voice activity atom"
                        let rpc =
                            if self.IsHost
                                then self.SyncVoiceActivityAtomClientRpc
                                else self.SyncVoiceActivityAtomServerRpc
                        logInfo "middle voice activity atom"
                        rpc(
                            toIdString payload.speakerId,
                            toIdType payload.speakerId,
                            payload.prob
                        )
                        logInfo "end voice activity atom"
                    | HeardAtom payload ->
                        logInfo "Predictor received HeardAtom payload"
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
    member _.Register(gameInput) =
        logInfo "Predictor.Register called"
        agent.Add gameInput

    [<ClientRpc>]
    member private _.SyncHeardAtomClientRpc(text, speakerClass, speakerClassType, speakerId, speakerIdType, sentenceId, elapsedTime, avgLogProb, noSpeechProb) =
        if shouldRegister() then
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
        if shouldRegister() then
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