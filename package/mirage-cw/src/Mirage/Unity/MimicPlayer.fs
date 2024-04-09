module Mirage.Unity.MimicPlayer

open MyceliumNetworking
open System
open System.Collections.Generic
open Mirage.Core.Field
open Mirage.Unity.RpcBehaviour
open Mirage.Core.Logger
open FSharpPlus
open Mirage.Core.Config

/// Holds what players that can be mimicked, to avoid duplicates.
let playerPool = new List<int>()

[<AllowNullLiteral>]
type MimicPlayer() as self =
    inherit RpcBehaviour()

    let random = Random()
    let MimickingPlayer = field()

    let isEnemyEnabled () =
        let config = getConfig()
        match self.transform.parent.gameObject.name.Replace("(Clone)", zero) with
            | "Toolkit_Whisk" -> config.toolkitWhisk
            | "Zombe" -> config.zombe
            | "Flicker" -> config.flicker
            | "Slurper" -> config.slurper
            | "Spider" -> config.spider
            | "BigSlap" -> config.bigSlap
            | "BigSlap_Small " -> config.bigSlapSmall
            | "Ear" -> config.ear
            | "Jello" -> config.jello
            | "Knifo" -> config.knifo
            | "Mouthe" -> config.mouthe
            | "Snatcho" -> config.snatcho
            | "Weeping" -> config.weeping
            | "BarnacleBall" -> config.barnacleBall
            | "Dog" -> config.dog
            | "EyeGuy" -> config.eyeGuy
            | "Bombs" -> config.bombs
            | "Larva" -> config.larva
            | _ -> false

    override this.Start() =
        base.Start()
        if this.IsHost && isEnemyEnabled() then
            let players = PlayerHandler.instance.players
            if playerPool.Count = 0 then
                playerPool.AddRange [0..players.Count]
            let index = random.Next playerPool.Count
            let playerIndex = playerPool[index]
            playerPool.RemoveAt index
            // If a player dc's and the player list is lower than the index in the player pool, cap it at the # of players instead.
            let mimickingPlayer = players[if playerIndex + 1 > players.Count then players.Count - 1 else playerIndex]
            set MimickingPlayer mimickingPlayer
            clientRpc this "MimicPlayerClientRpc" [|mimickingPlayer.refs.view.Owner.UserId|]

    [<CustomRPC>]
    member this.MimicPlayerClientRpc(userId) =
        if this.IsHost then
            PlayerHandler.instance.players
                |> find (fun player -> player.refs.view.Owner.UserId = userId)
                |> set MimickingPlayer
            if Option.isNone MimickingPlayer.Value then
                logError $"Failed to set mimicking player: {userId}"

    member _.GetMimickingPlayer() = getValue MimickingPlayer