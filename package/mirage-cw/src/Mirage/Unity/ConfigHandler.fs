module Mirage.Unity.ConfigHandler

open MyceliumNetworking
open Mirage.Unity.RpcBehaviour
open Mirage.Core.Config
open Mirage.Core.Field

[<AllowNullLiteral>]
type ConfigHandler() =
    inherit RpcBehaviour()

    static member val internal Instance = null with get, set

    override this.Awake() =
        base.Awake()
        ConfigHandler.Instance <- this

    override _.OnDestroy() =
        base.OnDestroy()
        ConfigHandler.Instance <- null

    /// Syncs the config to all clients. This only has an effect when invoked by the host.
    member this.SyncToAllClients() =
        if this.IsHost then
            let config = getConfig()
            clientRpc this "SyncConfigClientRpc"
                [|  config.mimicMinDelay
                    config.mimicMaxDelay
                    config.anglerMimic
                    config.streamer
                    config.infiltrator
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
                    config.muteLocalPlayerVoice
                |]

    [<CustomRPC>]
    member this.SyncConfigClientRpc(
        mimicMinDelay,
        mimicMaxDelay,
        anglerMimic,
        streamer,
        infiltrator,
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
        larva,
        muteLocalPlayerVoice
    ) =   
        if not this.IsHost then
            set SyncedConfig
                {   mimicMinDelay = mimicMinDelay
                    mimicMaxDelay = mimicMaxDelay
                    anglerMimic = anglerMimic
                    streamer = streamer
                    infiltrator = infiltrator
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
                    muteLocalPlayerVoice = muteLocalPlayerVoice
                }