module Mirage.Unity.MimicPlayer

open FSharpPlus
open System
open System.Collections.Generic
open Unity.Netcode
open Mirage.Unity.AudioStream
open Mirage.Domain.Logger
open Mirage.Domain.Config
open Mirage.Domain.Null

/// Holds what players that can be mimicked, to avoid duplicates.
let private playerPool = List<int>()

[<AllowNullLiteral>]
type MimicPlayer() =
    inherit NetworkBehaviour()

    let onSetMimicId = Event<Guid>()
    let random = Random()

    let mutable enemyAI = null
    let mutable mimicId = Guid.Empty
    let mutable mimickingPlayer = null

    let logMimic message = logInfo $"{enemyAI.GetType().Name}({mimicId}) - {message}"

    let rec randomPlayer () =
        let round = StartOfRound.Instance
        if playerPool.Count = 0 then
            playerPool.AddRange [0..round.connectedPlayersAmount]
        let index = random.Next playerPool.Count
        let playerId = playerPool[index]
        let player = StartOfRound.Instance.allPlayerScripts[playerId]
        playerPool.RemoveAt index
        // If a disconnected, the index might be out of bounds. In that case, fetch a new id.
        if playerId > round.connectedPlayersAmount || player.disconnectedMidGame then
            randomPlayer()
        else
            round.allPlayerScripts[playerId]

    member _.MimickingPlayer with get() = mimickingPlayer

    member _.MimicId with get() = mimicId

    /// This event is called when mimic id is set.
    [<CLIEvent>]
    member _.OnSetMimicId = onSetMimicId.Publish

    member this.Awake() = enemyAI <- this.GetComponent<EnemyAI>()

    member this.Start() =
        // StartMimicking for masked enemies gets run via a hook instead,
        // to ensure the mimickingPlayer is set before other mods try to interact with it.
        if not <| enemyAI :? MaskedPlayerEnemy then
            this.StartMimicking()

    member this.StartMimicking() =
        if this.IsHost then
            mimicId <- Guid.NewGuid()
            let isEnemyEnabled = Set.contains (stripConfigKey enemyAI.enemyType.enemyName) (getConfig().enemies)
            let mimickingPlayer =
                match enemyAI with
                    | :? MaskedPlayerEnemy as maskedEnemy when
                        isEnemyEnabled && random.Next(0, 100) < getConfig().maskedMimicChance ->
                        ValueSome <|
                            if isNull maskedEnemy.mimickingPlayer then randomPlayer()
                            else maskedEnemy.mimickingPlayer
                    | _ when
                        isEnemyEnabled
                            && not (enemyAI :? MaskedPlayerEnemy)
                            && not (enemyAI :? DressGirlAI)
                            && random.Next(0, 100) < getConfig().nonMaskedMimicChance ->
                        ValueSome <| randomPlayer()
                    | _ -> ValueNone
            flip iter mimickingPlayer <| fun player ->
                this.MimicPlayer(int player.playerClientId)

    /// Mimic the given player locally. An attached <b>MimicVoice</b> automatically uses the mimicked player for voices.
    member this.MimicPlayer playerId =
        let player = StartOfRound.Instance.allPlayerScripts[playerId]
        logMimic $"Mimicking player #{player.playerClientId}"
        mimickingPlayer <- player
        if enemyAI :? MaskedPlayerEnemy && getConfig().copyMaskedVisuals then
            let maskedEnemy = enemyAI :?> MaskedPlayerEnemy
            maskedEnemy.mimickingPlayer <- player
            maskedEnemy.SetSuit player.currentSuitID
            maskedEnemy.SetEnemyOutside(player.transform.position.y < -80f)
            maskedEnemy.SetVisibilityOfMaskedEnemy()
        this.GetComponent<AudioStream>().AllowedSenderId <- Some player.actualClientId
        if this.IsHost then
            this.MimicPlayerClientRpc(playerId, mimicId.ToString())
        onSetMimicId.Trigger mimicId

    [<ClientRpc>]
    member this.MimicPlayerClientRpc(playerId, mimicId') =
        if not this.IsHost then
            mimicId <- new Guid(mimicId')
            this.MimicPlayer playerId

    member this.ResetMimicPlayer() =
        logMimic "No longer mimicking a player."
        mimickingPlayer <- null
        if this.IsHost then
            this.ResetMimicPlayerClientRpc()

    [<ClientRpc>]
    member this.ResetMimicPlayerClientRpc() =
        if not this.IsHost then
            this.ResetMimicPlayer()

    member this.FixedUpdate() =
        if this.IsHost then
            // Set the mimicking player after the haunting player changes.
            // In singleplayer, the haunting player will always be the local player.
            // In multiplayer, the haunting player will always be the non-local player.
            if enemyAI :? DressGirlAI && Set.contains (stripConfigKey enemyAI.enemyType.enemyName) (getConfig().enemies) then
                let dressGirlAI = enemyAI :?> DressGirlAI
                let round = StartOfRound.Instance

                let rec randomPlayerNotHaunted () =
                    let player = randomPlayer()
                    if player = dressGirlAI.hauntingPlayer then
                        randomPlayerNotHaunted()
                    else
                        int player.playerClientId

                match ValueOption.ofObj dressGirlAI.hauntingPlayer, ValueOption.ofObj mimickingPlayer with
                    | ValueSome hauntingPlayer, ValueSome mimickingPlayer when hauntingPlayer = mimickingPlayer && round.connectedPlayersAmount > 0 ->
                        this.MimicPlayer <| randomPlayerNotHaunted()
                    | ValueSome hauntingPlayer, ValueNone ->
                        if round.connectedPlayersAmount = 0 then
                            this.MimicPlayer <| int hauntingPlayer.playerClientId
                        else
                            this.MimicPlayer <| randomPlayerNotHaunted()
                    | ValueNone, ValueSome _ ->
                        this.ResetMimicPlayer()
                    | _ -> ()