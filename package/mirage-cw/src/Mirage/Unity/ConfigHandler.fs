module Mirage.Unity.ConfigHandler

open FSharpPlus
open MyceliumNetworking
open Mirage.Unity.RpcBehaviour
open Mirage.Core.Config
open Mirage.Core.Field

type ConfigHandler() =
    inherit RpcBehaviour()

    /// Syncs the config to all clients. Should only be called by the host.
    member this.SyncToAllClients() =
        if this.IsHost then
            let config = getConfig()
            clientRpc this "SyncConfigClientRpc"
                [|  config.mimicMinDelay
                    config.mimicMaxDelay
                    config.imitateMode
                    config.toolkitWhisk
                    config.zombe
                    config.flicker
                    config.slurper
                    config.spider
                    config.bigSlap
                    config.bigSlapSmall
                    config.ear
                    config.jello
                    config.knifo
                    config.mouthe
                    config.snatcho
                    config.weeping
                    config.barnacleBall
                    config.dog
                    config.eyeGuy
                    config.bombs
                    config.larva
                |]

    [<CustomRPC>]
    member this.SyncConfigClientRpc(
        mimicMinDelay,
        mimicMaxDelay,
        imitateMode,
        toolkitWhisk,
        zombe,
        flicker,
        slurper,
        spider,
        bigSlap,
        bigSlapSmall,
        ear,
        jello,
        knifo,
        mouthe,
        snatcho,
        weeping,
        barnacleBall,
        dog,
        eyeGuy,
        bombs,
        larva
    ) =   
        if not this.IsHost then
            set SyncedConfig
                {   mimicMinDelay = mimicMinDelay
                    mimicMaxDelay = mimicMaxDelay
                    imitateMode = imitateMode
                    toolkitWhisk = toolkitWhisk
                    zombe = zombe
                    flicker = flicker
                    slurper = slurper
                    spider = spider
                    bigSlap = bigSlap
                    bigSlapSmall = bigSlapSmall
                    ear = ear
                    jello = jello
                    knifo = knifo
                    mouthe = mouthe
                    snatcho = snatcho
                    weeping = weeping
                    barnacleBall = barnacleBall
                    dog = dog
                    eyeGuy = eyeGuy
                    bombs = bombs
                    larva = larva
                }