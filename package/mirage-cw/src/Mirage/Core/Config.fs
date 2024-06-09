module Mirage.Core.Config

open System
open BepInEx.Configuration
open FSharpPlus
open Mirage.Core.Field

type internal LocalConfig(config: ConfigFile) =
    let [<Literal>] voiceMimicSection = "Mimic voice"
    let [<Literal>] enemySection = "Enemy"
    let [<Literal>] personalPreferenceSection = "Personal preferences"

    member val MimicMinDelay =
        config.Bind<int>(
            voiceMimicSection,
            "MinimumDelay",
            7000,
            "The minimum amount of time in between voice playbacks (in milliseconds)."
        )
    member val MimicMaxDelay =
        config.Bind<int>(
            voiceMimicSection,
            "MaximumDelay",
            12000,
            "The maximum amount of time in between voice playbacks (in milliseconds).\nThis only applies for masked enemies."
        )
    member val MuteLocalPlayerVoice =
        config.Bind<bool>(
            voiceMimicSection,
            "MuteLocalPlayerVoice",
            false,
            "If true, you can't hear your own voice from mimicking enemies while you are alive, but others can. When you die and become a spectator, you can hear your voice again.\n"
                + "If false, you will always be able to hear your own voice from mimicking enemies."
        )
    member val NeverDeleteVoiceClips =
        config.Bind<bool>(
            personalPreferenceSection,
            "NeverDeleteVoiceClips",
            false,
            "If set to false, voice clips will never get deleted. If set to false, voice clips are deleted upon closing the game.\n"
                + "Since this setting is a personal preference, it is not synced to other players."
        )
    member val LocalPlayerVolume =
        config.Bind<float32>(
            personalPreferenceSection,
            "LocalPlayerVolume",
            1f,
            ConfigDescription(
                "The volume for the local player's mimicked voice. This is setting is not synced, since the volume you want to use is personal preference.",
                AcceptableValueRange<float32>(0f, 1f)
            )
        )
    member val AnglerMimic =
        config.Bind<bool>(
            enemySection,
            "AnglerMimic",
            true
        )
    member val Streamer =
        config.Bind<bool>(
            enemySection,
            "Streamer",
            true
        )
    member val Infiltrator =
        config.Bind<bool>(
            enemySection,
            "Infiltrator",
            true
        )
    member val ToolkitWhisk =
        config.Bind<bool>(
            enemySection,
            "Toolkit_Whisk",
            false
        )
    member val Zombe =
        config.Bind<bool>(
            enemySection,
            "Zombe",
            true
        )
    member val Flicker =
        config.Bind<bool>(
            enemySection,
            "Flicker",
            false
        )
    member val Slurper =
        config.Bind<bool>(
            enemySection,
            "Slurper",
            false
        )
    member val Spider =
        config.Bind<bool>(
            enemySection,
            "Spider",
            false
        )
    member val BigSlap =
        config.Bind<bool>(
            enemySection,
            "BigSlap",
            false
        )
    member val BigSlapSmall =
        config.Bind<bool>(
            enemySection,
            "BigSlap_Small",
            false
        )
    member val Ear =
        config.Bind<bool>(
            enemySection,
            "Ear",
            false
        )
    member val Jello =
        config.Bind<bool>(
            enemySection,
            "Jello",
            true
        )
    member val Knifo =
        config.Bind<bool>(
            enemySection,
            "Knifo",
            false
        )
    member val Mouthe =
        config.Bind<bool>(
            enemySection,
            "Mouthe",
            false
        )
    member val Snatcho =
        config.Bind<bool>(
            enemySection,
            "Snatcho",
            false
        )
    member val Weeping =
        config.Bind<bool>(
            enemySection,
            "Weeping",
            false
        )
    member val BarnacleBall =
        config.Bind<bool>(
            enemySection,
            "BarnacleBall",
            false
        )
    member val Dog =
        config.Bind<bool>(
            enemySection,
            "Dog",
            true
        )
    member val EyeGuy =
        config.Bind<bool>(
            enemySection,
            "EyeGuy",
            false
        )
    member val Bombs =
        config.Bind<bool>(
            enemySection,
            "Bombs",
            false
        )
    member val Larva =
        config.Bind<bool>(
            enemySection,
            "Larva",
            false
        )

/// <summary>
/// Network synchronized configuration values. This is taken from the wiki:
/// https://lethal.wiki/dev/intermediate/custom-config-syncing
/// </summary>
[<Serializable>]
type internal SyncedConfig =
    {   mimicMinDelay: int
        mimicMaxDelay: int
        anglerMimic: bool
        streamer: bool
        infiltrator: bool
        toolkitWhisk: bool
        zombe: bool
        flicker: bool
        slurper: bool
        spider: bool
        bigSlap: bool
        bigSlapSmall: bool
        ear: bool
        jello: bool
        knifo: bool
        mouthe: bool
        snatcho: bool
        weeping: bool
        barnacleBall: bool
        dog: bool
        eyeGuy: bool
        bombs: bool
        larva: bool
        muteLocalPlayerVoice: bool
    }

let private toSyncedConfig (config: LocalConfig) =
    {   mimicMinDelay = config.MimicMinDelay.Value
        mimicMaxDelay = config.MimicMaxDelay.Value
        anglerMimic = config.AnglerMimic.Value
        streamer = config.Streamer.Value
        infiltrator = config.Infiltrator.Value
        toolkitWhisk = config.ToolkitWhisk.Value
        zombe = config.Zombe.Value
        flicker = config.Flicker.Value
        slurper = config.Slurper.Value
        spider = config.Spider.Value
        bigSlap = config.BigSlap.Value
        bigSlapSmall = config.BigSlapSmall.Value
        ear = config.Ear.Value
        jello = config.Jello.Value
        knifo = config.Knifo.Value
        mouthe = config.Mouthe.Value
        snatcho = config.Snatcho.Value
        weeping = config.Weeping.Value
        barnacleBall = config.BarnacleBall.Value
        dog = config.Dog.Value
        eyeGuy = config.EyeGuy.Value
        bombs = config.EyeGuy.Value
        larva = config.Larva.Value
        muteLocalPlayerVoice = config.MuteLocalPlayerVoice.Value
    }

/// <summary>
/// An action for synchronizing the <b>SyncedConfig</b>.
/// </summary>
type internal SyncAction = RequestSync | ReceiveSync

let private LocalConfig = field<LocalConfig>()
let internal SyncedConfig = field<SyncedConfig>()

/// <summary>
/// Retrieves a <b>LocalConfig</b>, containing the local player's configuration.<br />
/// If you need a syned config, use <b>getConfig()</b>.
/// </summary>
let internal getLocalConfig () =
    let errorIfMissing () =
        invalidOp "Failed to retrieve local config. This is probably due to not running initConfig."
    Option.defaultWith errorIfMissing  <| getValue LocalConfig

/// <summary>
/// Retrieves a <b>SyncedConfig</b>, either from being synced with the host, or taken by the local config.<br />
/// This requires <b>initConfig</b> to be invoked to work.
/// </summary>
let internal getConfig () = Option.defaultWith (toSyncedConfig << getLocalConfig) <| getValue SyncedConfig

let initConfig (file: ConfigFile) =
    monad' {
        if Option.isNone LocalConfig.Value then
            let config = new LocalConfig(file)
            let minDelayKey = config.MimicMinDelay.Definition.Key
            let maxDelayKey = config.MimicMaxDelay.Definition.Key
            let errorHeader = "Configuration is invalid. "
            if config.MimicMinDelay.Value < 0 then
                return! Error $"{errorHeader}{minDelayKey} cannot have a value smaller than 0."
            if config.MimicMaxDelay.Value < 0 then
                return! Error $"{errorHeader}{maxDelayKey} cannot have a value smaller than 0."
            if config.MimicMinDelay.Value > config.MimicMaxDelay.Value then
                return! Error $"{errorHeader}{minDelayKey} must have a value smaller than {maxDelayKey}"
            set LocalConfig config
    }