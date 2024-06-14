module Mirage.Unity.MimicPlayer

open System
open System.Collections.Generic
open FSharpPlus
open GameNetcodeStuff
open Unity.Netcode
open Unity.Collections
open Mirage.Domain.Logger
open Mirage.Unity.AudioStream

/// Holds what players that can be mimicked, to avoid duplicates.
let private playerPool = new List<int>()

/// <summary>
/// A component that attaches to an <b>EnemyAI</b> to mimic a player.
/// If the attached enemy is a <b>MaskedPlayerEnemy</b>, this will also copy its visuals.
/// </summary>
[<AllowNullLiteral>]
type MimicPlayer() =
    inherit NetworkBehaviour()

    let mimicId = new NetworkVariable<FixedString64Bytes>()

    let random = new Random()
    let mutable enemyAI: EnemyAI = null
    let mutable mimickingPlayer: PlayerControllerB = null

    let logMimic message = logInfo $"{enemyAI.GetType().Name}({mimicId.Value.Value}) - {message}"

    let rec randomPlayer () =
        let round = StartOfRound.Instance
        if playerPool.Count = 0 then
            playerPool.AddRange [0..round.connectedPlayersAmount]
        let index = random.Next playerPool.Count
        let playerId = playerPool[index]
        let player = StartOfRound.Instance.allPlayerScripts[playerId]
        playerPool.RemoveAt index
        // If a disconnected, the index might be out of bounds. In that case, fetch a new id.
        // This is a simple band-aid fix and isn't ideal, but this'll do for now.
        if playerId > round.connectedPlayersAmount || player.disconnectedMidGame then
            randomPlayer()
        else
            round.allPlayerScripts[playerId]

    let mimicPlayer (player: PlayerControllerB) (maskedEnemy: MaskedPlayerEnemy) =
        if not (isNull maskedEnemy) then
            maskedEnemy.mimickingPlayer <- player
            maskedEnemy.SetSuit player.currentSuitID
            // Why >= -80f? This is taken from MaskedPlayerEnemy.killAnimation()
            maskedEnemy.SetEnemyOutside(player.transform.position.y >= -80f)
            maskedEnemy.SetVisibilityOfMaskedEnemy()

    member _.MimickingPlayer with get() = mimickingPlayer

    member this.Awake() = enemyAI <- this.GetComponent<EnemyAI>()

    member this.Start() =
        if this.IsHost then
            mimicId.Value <- FixedString64Bytes(Guid.NewGuid().ToString())
        // StartMimicking for masked enemies gets run via a patch instead,
        // to ensure the mimickingPlayer is set before other mods try to interact with it.
        if isNull <| this.GetComponent<MaskedPlayerEnemy>() then
            this.StartMimicking()

    override this.OnNetworkSpawn() =
        let onMimicIdChanged _ mimicId =
            // TODO: mimicInit
            ()
        mimicId.OnValueChanged <- onMimicIdChanged

    member _.GetMimicId() = new Guid(mimicId.Value.Value)

    member this.StartMimicking() =
        if this.IsHost then
            let maskedEnemy = this.GetComponent<MaskedPlayerEnemy>()
            let mimickingPlayer =
                if (enemyAI : EnemyAI) :? MaskedPlayerEnemy then
                    if isNull maskedEnemy.mimickingPlayer then Some <| randomPlayer()
                    else Some maskedEnemy.mimickingPlayer
                //else if mimicEnemyEnabled enemyAI then Some <| randomPlayer()
                else None
            flip iter mimickingPlayer <| fun player ->
                this.MimicPlayer <| int player.playerClientId

    /// <summary>
    /// Mimic the given player locally. An attached <b>MimicVoice</b> automatically uses the mimicked player for voices.
    /// </summary>
    member this.MimicPlayer(playerId) =
        let player = StartOfRound.Instance.allPlayerScripts[playerId]
        logMimic $"Mimicking player #{player.playerClientId}"
        mimickingPlayer <- player
        mimicPlayer player <| this.GetComponent<MaskedPlayerEnemy>()
        this.GetComponent<AudioStream>().AllowedSenderId <- Some player.actualClientId
        if this.IsHost then
            this.MimicPlayerClientRpc playerId

    [<ClientRpc>]
    member this.MimicPlayerClientRpc(playerId) =
        if not this.IsHost then
            this.MimicPlayer(playerId)

    member this.ResetMimicPlayer() =
        logMimic "No longer mimicking a player."
        mimickingPlayer <- null
        if this.IsHost then
            this.ResetMimicPlayerClientRpc()

    [<ClientRpc>]
    member this.ResetMimicPlayerClientRpc() =
        if not this.IsHost then
            this.ResetMimicPlayer()

    member this.Update() =
        if this.IsHost then
            // Set the mimicking player after the haunting player changes.
            // In singleplayer, the haunting player will always be the local player.
            // In multiplayer, the haunting player will always be the non-local player.
            if enemyAI :? DressGirlAI then
                let dressGirlAI = enemyAI :?> DressGirlAI
                let round = StartOfRound.Instance

                let rec randomPlayerNotHaunted () =
                    let player = randomPlayer()
                    if player = dressGirlAI.hauntingPlayer then
                        randomPlayerNotHaunted()
                    else
                        int player.playerClientId

                match (Option.ofObj dressGirlAI.hauntingPlayer, Option.ofObj mimickingPlayer) with
                    | (Some hauntingPlayer, Some mimickingPlayer) when hauntingPlayer = mimickingPlayer && round.connectedPlayersAmount > 0 ->
                        this.MimicPlayer <| randomPlayerNotHaunted()
                    | (Some hauntingPlayer, None) ->
                        if round.connectedPlayersAmount = 0 then
                            this.MimicPlayer <| int hauntingPlayer.playerClientId
                        else
                            this.MimicPlayer <| randomPlayerNotHaunted()
                    | (None, Some _) ->
                        this.ResetMimicPlayer()
                    | _ -> ()