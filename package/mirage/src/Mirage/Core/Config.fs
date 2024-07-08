module Mirage.Core.Config

open BepInEx.Configuration
open FSharpPlus
open System
open System.IO
open Unity.Collections
open Unity.Netcode
open Mirage.PluginInfo
open Mirage.Core.Field
open Mirage.Core.Logger
open System.Runtime.Serialization

/// <summary>
/// Local preferences managed by BepInEx.
/// </summary>
type internal LocalConfig(config: ConfigFile) =
    let [<Literal>] imitateSection = "Imitate player"
    let [<Literal>] maskedSection = "MaskedPlayerEnemy"
    member val ImitateMinDelay =
        config.Bind<int>(
            imitateSection,
            "MinimumDelay",
            7000,
            "The minimum amount of time in between voice playbacks (in milliseconds).\nThis only applies for masked enemies."
        )
    member val ImitateMaxDelay =
        config.Bind<int>(
            imitateSection,
            "MaximumDelay",
            12000,
            "The maximum amount of time in between voice playbacks (in milliseconds).\nThis only applies for masked enemies."
        )
    member val ImitateMinDelayNonMasked =
        config.Bind<int>(
            imitateSection,
            "MinimumDelayNonMasked",
            20000,
            "The minimum amount of time in between voice playbacks (in milliseconds).\nThis only applies for non-masked enemies."
        )
    member val ImitateMaxDelayNonMasked =
        config.Bind<int>(
            imitateSection,
            "MaximumDelayNonMasked",
            40000,
            "The maximum amount of time in between voice playbacks (in milliseconds).\nThis only applies for non-masked enemies."
        )
    member val ImitateMode =
        config.Bind<string>(
            imitateSection,
            "ImitateMode",
            "NoRepeat",
            "Possible values: Random, NoRepeat\n"
                + "Random: Recordings are randomly picked.\n"
                + "NoRepeat: Recordings are randomly picked, except the recording can only be played once until no more recordings remain, which can then be replayed."
        )
    member val DeleteRecordingsPerRound =
        config.Bind<bool>(
            imitateSection,
            "DeleteRecordingsPerRound",
            false,
            "Set to true to have recordings deleted in between rounds (after pulling the lever). Set to false to delete only delete when closing the game."
        )
    member val IgnoreRecordingsDeletion =
        config.Bind<bool>(
            imitateSection,
            "IgnoreRecordingsDeletion",
            false,
            "Set to true to never delete recordings (ignores the option set by DeleteRecordingsPerRound).\n"
                + "Note: This only applies to you. Each player who wants to use this setting will have to set it themselves."
        )
    member val RecordWhileDead =
        config.Bind<bool>(
            imitateSection,
            "RecordWhileDead",
            false,
            "Whether or not a player should be recorded while they're dead."
        )
    member val MuteLocalPlayerVoice =
        config.Bind<bool>(
            imitateSection,
            "MuteLocalPlayerVoice",
            false,
            "If true, you can't hear your own voice from mimicking enemies while you are alive, but others can. When you die and become a spectator, you can hear your voice again.\n"
                + "If false, you will always be able to hear your own voice from mimicking enemies."
        )
    member val LocalPlayerVolume =
        config.Bind<float32>(
            imitateSection,
            "LocalPlayerVolume",
            1f,
            "The volume for the local player's mimicked voice. This is setting is not synced, since the volume you want to use is personal preference. Must have a value of 0-1."
        )
    member val EnableMaskedEnemy =
        config.Bind<bool>(
            imitateSection,
            "EnableMaskedEnemy",
            true,
            "Whether or not the masked enemy should mimic voices."
        )
    member val EnableBaboonHawk =
        config.Bind<bool>(
            imitateSection,
            "EnableBaboonHawk",
            false,
            "Whether or not the baboon hawk should mimic voices."
        )
    member val EnableBracken =
        config.Bind<bool>(
            imitateSection,
            "EnableBracken",
            false,
            "Whether or not the bracken should mimic voices."
        )
    member val EnableSpider =
        config.Bind<bool>(
            imitateSection,
            "EnableSpider",
            false,
            "Whether or not the spider should mimic voices."
        )
    member val EnableBees =
        config.Bind<bool>(
            imitateSection,
            "EnableBees",
            false,
            "Whether or not bees should mimic voices."
        )
    member val EnableLocustSwarm =
        config.Bind<bool>(
            imitateSection,
            "EnableLocustSwarm",
            false,
            "Whether or not locust swarms should mimic voices."
        )
    member val EnableCoilHead =
        config.Bind<bool>(
            imitateSection,
            "EnableCoilHead",
            false,
            "Whether or not the coil-head should mimic voices."
        )
    member val EnableEarthLeviathan =
        config.Bind<bool>(
            imitateSection,
            "EnableEarthLeviathan",
            false,
            "Whether or not the earth leviathan should mimic voices."
        )
    member val EnableEyelessDog =
        config.Bind<bool>(
            imitateSection,
            "EnableEyelessDog",
            false,
            "Whether or not the eyeless dog should mimic voices."
        )
    member val EnableForestKeeper =
        config.Bind<bool>(
            imitateSection,
            "EnableForestKeeper",
            false,
            "Whether or not the forest keeper should mimic voices."
        )
    member val EnableGhostGirl =
        config.Bind<bool>(
            imitateSection,
            "EnableGhostgirl",
            false,
            "Whether or not the ghost girl should mimic voices."
        )
    member val EnableHoardingBug =
        config.Bind<bool>(
            imitateSection,
            "EnableHoardingBug",
            false,
            "Whether or not the hoarding bug should mimic voices."
        )
    member val EnableHygrodere =
        config.Bind<bool>(
            imitateSection,
            "EnableHygrodere",
            false,
            "Whether or not the hygrodere should mimic voices."
        )
    member val EnableJester =
        config.Bind<bool>(
            imitateSection,
            "EnableJester",
            false,
            "Whether or not the jester should mimic voices."
        )
    member val EnableManticoil =
        config.Bind<bool>(
            imitateSection,
            "EnableManticoil",
            false,
            "Whether or not the manticoil should mimic voices."
        )
    member val EnableNutcracker =
        config.Bind<bool>(
            imitateSection,
            "EnableNutcracker",
            false,
            "Whether or not the nutcracker should mimic voices."
        )
    member val EnableSnareFlea =
        config.Bind<bool>(
            imitateSection,
            "EnableSnareFlea",
            false,
            "Whether or not the snare flea should mimic voices."
        )
    member val EnableSporeLizard =
        config.Bind<bool>(
            imitateSection,
            "EnableSporeLizard",
            false,
            "Whether or not the spore lizard should mimic voices."
        )
    member val EnableThumper =
        config.Bind<bool>(
            imitateSection,
            "EnableThumper",
            false,
            "Whether or not the thumper should mimic voices."
        )
    member val EnableButlerBees =
        config.Bind<bool>(
            imitateSection,
            "EnableButlerBees",
            false,
            "Whether or not butler bees should mimic voices."
        )
    member val EnableButler =
        config.Bind<bool>(
            imitateSection,
            "EnableButler",
            false,
            "Whether or not the butler should mimic voices."
        )
    member val EnableFlowerSnake =
        config.Bind<bool>(
            imitateSection,
            "EnableFlowerSnake",
            false,
            "Whether or not the flower snake should mimic voices."
        )
    member val EnableOldBird =
        config.Bind<bool>(
            imitateSection,
            "EnableOldBird",
            false,
            "Whether or not the old bird should mimic voices."
        )
    member val EnableClaySurgeon =
        config.Bind<bool>(
            imitateSection,
            "EnableClaySurgeon",
            false,
            "Whether or not the clay surgeon should mimic voices."
        )
    member val EnableBushWolf =
        config.Bind<bool>(
            imitateSection,
            "EnableBushWolf",
            false,
            "Whether or not the bush wolf should mimic voices."
        )
    member val EnableModdedEnemies =
        config.Bind<bool>(
            imitateSection,
            "EnableModdedEnemies",
            false,
            "Whether or not all modded enemies should mimic voices."
        )
    member val EnablePenalty =
        config.Bind<bool>(
            "Credits",
            "EnablePenalty",
            true,
            "Whether the credits penalty should be applied during the end of a round. Set this to true to have the default vanilla behaviour.\n"
                + "This setting is only left here for legacy reasons, and will be removed in a future release."
        )
    member val EnableOverrideSpawnChance =
        config.Bind<bool>(
            maskedSection,
            "EnableOverrideSpawnChance",
            true,
            "Whether or not the OverrideSpawnChance value should be used. If false, masked enemy spawn weights will be untouched."
        )
    member val OverrideSpawnChance =
        config.Bind<int>(
            maskedSection,
            "OverrideSpawnChance",
            15,
            "The percentage chance a masked enemy should naturally spawn. Spawn weights are internally calculated and modified based on this value. Must have a value of 0-100"
        )
    member val UseCustomSpawnCurve =
        config.Bind<bool>(
            maskedSection,
            "UseCustomSpawnCurve",
            true,
            "Uses a custom spawn curve that makes masked enemies spawn later in the day, rather than the default way masked enemies spawn.\nNote: This is only used when EnableOverrideSpawnChance is enabled."
        )
    member val MaxMasked =
        config.Bind<int>(
            maskedSection,
            "MaxMasked",
            2,
            "Maximum number of natural spawns a masked enemy should have."
        )
    member val SpawnOnPlayerDeath =
        config.Bind<int>(
            maskedSection,
            "SpawnOnPlayerDeath",
            10,
            "The percent chance of a masked enemy spawning on player death (like a zombie). Must have a value of 0-100.\nSet this to 0 to disable this feature."
        )
    member val SpawnOnlyWhenPlayerAlone =
        config.Bind<bool>(
            maskedSection,
            "SpawnOnlyWhenPlayerAlone",
            true,
            "If set to true, SpawnOnPlayerDeath will only succeed if the dying player is alone."
        )
    member val EnableMask =
        config.Bind<bool>(
            maskedSection,
            "EnableMask",
            false,
            "Whether or not a masked enemy should have its mask texture"
        )
    member val EnableArmsOut =
        config.Bind<bool>(
            maskedSection,
            "EnableArmsOut",
            false,
            "Whether or not the arms out animation should be used."
        )

type ImitateMode = ImitateRandom | ImitateNoRepeat

/// <summary>
/// Network synchronized configuration values. This is taken from the wiki:
/// https://lethal.wiki/dev/intermediate/custom-config-syncing
/// </summary>
[<Serializable>]
type internal SyncedConfig =
    {   imitateMinDelay: int
        imitateMaxDelay: int
        imitateMinDelayNonMasked: int
        imitateMaxDelayNonMasked: int
        imitateMode: ImitateMode
        muteLocalPlayerVoice: bool
        deleteRecordingsPerRound: bool
        recordWhileDead: bool
        enableMaskedEnemy: bool
        enableBaboonHawk: bool
        enableBracken: bool
        enableSpider: bool
        enableBees: bool
        enableLocustSwarm: bool
        enableCoilHead: bool
        enableEarthLeviathan: bool
        enableEyelessDog: bool
        enableForestKeeper: bool
        enableGhostGirl: bool
        enableHoardingBug: bool
        enableHygrodere: bool
        enableJester: bool
        enableManticoil: bool
        enableNutcracker: bool
        enableSnareFlea: bool
        enableSporeLizard: bool
        enableThumper: bool
        enableButlerBees: bool
        enableButler: bool
        enableFlowerSnake: bool
        enableOldBird: bool
        enableClaySurgeon: bool
        enableBushWolf: bool
        enableModdedEnemies: bool
        enablePenalty: bool
        enableOverrideSpawnChance: bool
        overrideSpawnChance: int
        useCustomSpawnCurve: bool
        spawnOnPlayerDeath: int
        spawnOnlyWhenPlayerAlone: bool
        enableMask: bool
        enableArmsOut: bool
    }

let private toSyncedConfig (config: LocalConfig) =
    {   imitateMinDelay = config.ImitateMinDelay.Value
        imitateMaxDelay = config.ImitateMaxDelay.Value
        imitateMinDelayNonMasked = config.ImitateMinDelayNonMasked.Value
        imitateMaxDelayNonMasked = config.ImitateMaxDelayNonMasked.Value
        imitateMode =
            match config.ImitateMode.Value.ToLower() with
                | "random" -> ImitateRandom
                | "norepeat" -> ImitateNoRepeat
                | mode -> invalidOp $"Synced invalid ImitateMode value: {mode}"
        muteLocalPlayerVoice = config.MuteLocalPlayerVoice.Value
        deleteRecordingsPerRound = config.DeleteRecordingsPerRound.Value
        recordWhileDead = config.RecordWhileDead.Value
        enableMaskedEnemy = config.EnableMaskedEnemy.Value
        enableBaboonHawk = config.EnableBaboonHawk.Value
        enableBracken = config.EnableBracken.Value
        enableSpider = config.EnableSpider.Value
        enableBees = config.EnableBees.Value
        enableLocustSwarm = config.EnableLocustSwarm.Value
        enableCoilHead = config.EnableCoilHead.Value
        enableEarthLeviathan = config.EnableEarthLeviathan.Value
        enableEyelessDog = config.EnableEyelessDog.Value
        enableForestKeeper = config.EnableForestKeeper.Value
        enableGhostGirl = config.EnableGhostGirl.Value
        enableHoardingBug = config.EnableHoardingBug.Value
        enableHygrodere = config.EnableHygrodere.Value
        enableJester = config.EnableJester.Value
        enableManticoil = config.EnableManticoil.Value
        enableNutcracker = config.EnableNutcracker.Value
        enableSnareFlea = config.EnableSnareFlea.Value
        enableSporeLizard = config.EnableSporeLizard.Value
        enableThumper = config.EnableThumper.Value
        enableButlerBees = config.EnableButlerBees.Value
        enableButler = config.EnableButler.Value
        enableFlowerSnake = config.EnableFlowerSnake.Value
        enableOldBird = config.EnableOldBird.Value
        enableClaySurgeon = config.EnableClaySurgeon.Value
        enableBushWolf = config.EnableBushWolf.Value
        enableModdedEnemies = config.EnableModdedEnemies.Value
        enablePenalty = config.EnablePenalty.Value
        enableOverrideSpawnChance = config.EnableOverrideSpawnChance.Value
        overrideSpawnChance = config.OverrideSpawnChance.Value
        useCustomSpawnCurve = config.UseCustomSpawnCurve.Value
        spawnOnPlayerDeath = config.SpawnOnPlayerDeath.Value
        spawnOnlyWhenPlayerAlone = config.SpawnOnlyWhenPlayerAlone.Value
        enableMask = config.EnableMask.Value
        enableArmsOut = config.EnableArmsOut.Value
    }

/// <summary>
/// An action for synchronizing the <b>SyncedConfig</b>.
/// </summary>
type internal SyncAction = RequestSync | ReceiveSync

/// <summary>
/// Convert the action to the message event name.
/// </summary>
let private toNamedMessage (action: SyncAction) =
    match action with
        | RequestSync -> $"{pluginId}_OnRequestConfigSync"
        | ReceiveSync -> $"{pluginId}_OnReceiveConfigSync"

let private messageManager () = NetworkManager.Singleton.CustomMessagingManager
let private isClient () = NetworkManager.Singleton.IsClient
let private isHost () = NetworkManager.Singleton.IsHost

let private LocalConfig = field()
let private SyncedConfig = field()

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

/// <summary>
/// Initialize the configuration. Does nothing if you run it more than once.
/// </summary>
let initConfig (file: ConfigFile) =
    monad' {
        if Option.isNone LocalConfig.Value then
            let config = new LocalConfig(file)
            let errorHeader = "Configuration is invalid. "
            let minDelayKey = config.ImitateMinDelay.Definition.Key
            let maxDelayKey = config.ImitateMaxDelay.Definition.Key
            let spawnOnPlayerDeathKey = config.SpawnOnPlayerDeath.Definition.Key
            if config.ImitateMinDelay.Value < 0 then
                return! Error $"{errorHeader}{minDelayKey} cannot have a value smaller than 0."
            if config.ImitateMaxDelay.Value < 0 then
                return! Error $"{errorHeader}{maxDelayKey} cannot have a value smaller than 0."
            if config.ImitateMinDelay.Value > config.ImitateMaxDelay.Value then
                return! Error $"{errorHeader}{minDelayKey} must have a value smaller than {maxDelayKey}"
            if not <| exists ((=) (String.toLower config.ImitateMode.Value)) ["random"; "norepeat"] then
                return! Error $"{errorHeader}{config.ImitateMode.Definition.Key} is set to an invalid value. Refer to the config for possible values."
            if config.SpawnOnPlayerDeath.Value < 0 || config.SpawnOnPlayerDeath.Value > 100 then
                return! Error $"{errorHeader}{spawnOnPlayerDeathKey} must have a value between 0-100."
            if config.LocalPlayerVolume.Value < 0f || config.LocalPlayerVolume.Value > 1f then
                return! Error $"{errorHeader}{config.LocalPlayerVolume.Definition.Key} must have a value between 0.0-1.0"
            if config.OverrideSpawnChance.Value < 0 || config.OverrideSpawnChance.Value > 100 then
                return! Error $"{errorHeader}{config.OverrideSpawnChance.Definition.Key} mut have a value between 0-100."
            set LocalConfig config
    }

let private serializeToBytes<'A> (value: 'A) : array<byte> =
    let serializer = new DataContractSerializer(typeof<'A>)
    use stream = new MemoryStream()
    try
        serializer.WriteObject(stream, value)
        stream.ToArray()
    with | error ->
        logError $"Failed to serialize value: {error}"
        null

let private deserializeFromBytes<'A> (data: array<byte>) : 'A =
    let serializer = new DataContractSerializer(typeof<'A>)
    use stream = new MemoryStream(data)
    try
        serializer.ReadObject stream :?> 'A
    with | error ->
        logError $"Failed to deserialize bytes: {error}"
        Unchecked.defaultof<'A>

let private sendMessage action (clientId: uint64) (stream: FastBufferWriter) =
    let MaxBufferSize = 1300
    let delivery =
        if stream.Capacity > MaxBufferSize
            then NetworkDelivery.ReliableFragmentedSequenced
            else NetworkDelivery.Reliable
    messageManager().SendNamedMessage(toNamedMessage action, clientId, stream, delivery)

/// <summary>
/// Revert the synchronized config and use the default values.
/// </summary>
let revertSync () = setNone SyncedConfig

/// <summary>
/// Request to synchronize the local config with the host.
/// </summary>
let requestSync () =
    if isClient() then
        use stream = new FastBufferWriter(sizeof<int32>, Allocator.Temp) 
        sendMessage RequestSync 0UL stream

let private onRequestSync clientId _ =
    if isHost() then
        let bytes = serializeToBytes <| getConfig()
        let bytesLength = bytes.Length
        use writer = new FastBufferWriter(bytesLength + sizeof<int32>, Allocator.Temp)
        try
            writer.WriteValueSafe &bytesLength
            writer.WriteBytesSafe bytes
            sendMessage ReceiveSync clientId writer
        with | error ->
            logError $"Failed during onRequestSync: {error}"

let private onReceiveSync _ (reader: FastBufferReader) =
    if not <| isHost() then
        handleResult <| monad' {
            if not <| reader.TryBeginRead sizeof<int> then
                return! Error "onReceiveSync failed while reading beginning of buffer."
            let mutable bytesLength = 0
            reader.ReadValueSafe &bytesLength
            if not <| reader.TryBeginRead(bytesLength) then
                return! Error "onReceiveSync failed. Host could not synchronize config."
            let bytes = Array.zeroCreate<byte> bytesLength
            reader.ReadBytesSafe(ref bytes, bytesLength)
            set SyncedConfig <| deserializeFromBytes bytes
        }

/// <summary>
/// Register the named message handler for the given action.
/// </summary>
let internal registerHandler action =
    let message = toNamedMessage action
    let register handler = messageManager().RegisterNamedMessageHandler(message, handler)
    let callback =
        match action with
            | RequestSync -> onRequestSync
            | ReceiveSync -> onReceiveSync
    register callback