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
let playerPool = new List<Player>()

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
                playerPool.AddRange players
            let index = random.Next playerPool.Count
            let mimickingPlayer = playerPool[index]
            playerPool.RemoveAt index
            set MimickingPlayer mimickingPlayer
            clientRpc this "MimicPlayerClientRpc" [|mimickingPlayer.refs.view.ViewID|]

    [<CustomRPC>]
    member this.MimicPlayerClientRpc(viewId) =
        if this.IsHost then
            PlayerHandler.instance.players
                |> find (fun player -> player.refs.view.ViewID = viewId)
                |> set MimickingPlayer
            if Option.isNone MimickingPlayer.Value then
                logError $"Failed to set mimicking player. ViewId: {viewId}"

    member _.GetMimickingPlayer() = getValue MimickingPlayer