module Mirage.Unity.MimicPlayer

open FSharpPlus
open System
open System.Collections.Generic
open MyceliumNetworking
open Mirage.Core.Field
open Mirage.Unity.RpcBehaviour
open Mirage.Core.Logger
open Mirage.Core.Config
open Mirage.Core.Monad
open AudioStream

/// Holds what players that can be mimicked, to avoid duplicates.
let playerPool = new List<Player>()

[<AllowNullLiteral>]
type MimicPlayer() as self =
    inherit RpcBehaviour()

    let random = Random()
    let MimickingPlayer = field()
    let AudioStream = field<AudioStream>()

    let isEnemyEnabled () =
        let config = getConfig()
        match self.transform.parent.gameObject.name.Replace("(Clone)", zero) with
            | "AnglerMimic" -> config.anglerMimic
            | "Streamer" -> config.streamer
            | "Infiltrator2" -> config.infiltrator
            | "Toolkit_Whisk" -> config.toolkitWhisk
            | "Zombe" -> config.zombe
            | "Flicker" -> config.flicker
            | "Slurper" -> config.slurper
            | "Spider" -> config.spider
            | "BigSlap" -> config.bigSlap
            | "BigSlap_Small" -> config.bigSlapSmall
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

    override this.Awake() =
        base.Awake()
        set AudioStream <| this.GetComponent<AudioStream>()

    member this.Start() =
        if this.IsHost && isEnemyEnabled() then
            let players = PlayerHandler.instance.players
            if playerPool.Count = 0 then
                playerPool.AddRange players
            let index = random.Next playerPool.Count
            let mimickingPlayer = playerPool[index]
            playerPool.RemoveAt index
            set MimickingPlayer mimickingPlayer
            this.GetComponent<AudioStream>().SetAudioMixer mimickingPlayer
            runAsync self.destroyCancellationToken <| async {
                // Bandaid fix to wait for network objects to instantiate on clients, since Mycelium doesn't handle this edge-case yet.
                do! Async.Sleep 2000
                clientRpc this "MimicPlayerClientRpc" [|mimickingPlayer.refs.view.ViewID|]
            }

    [<CustomRPC>]
    member this.MimicPlayerClientRpc(viewId) =
        handleResult <| monad' {
            if not this.IsHost then
                let player =
                    PlayerHandler.instance.players
                        |> tryFind (fun player -> player.refs.view.ViewID = viewId)
                setOption MimickingPlayer player
                let! audioStream = getter "MimicPlayer" AudioStream "AudioStream" "MimicPlayerClientRpc"
                match MimickingPlayer.Value with
                    | None -> logError $"Failed to set mimicking player. ViewId: {viewId}"
                    | Some player -> audioStream.SetAudioMixer player
        }

    member _.GetMimickingPlayer() = getValue MimickingPlayer