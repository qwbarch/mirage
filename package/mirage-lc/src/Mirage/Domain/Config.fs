module Mirage.Domain.Config

open BepInEx
open FSharpPlus
open System
open System.IO
open System.Runtime.Serialization
open BepInEx.Configuration
open Unity.Netcode
open Unity.Collections
open Mirage.Prelude
open Mirage.PluginInfo
open Mirage.Domain.Logger

let private loadConfig configName = ConfigFile(Path.Combine(Paths.ConfigPath, $"Mirage.{configName}.cfg"), true)

type LocalConfig(general: ConfigFile, enemies: ConfigFile) =
    let bind section key value (description: ConfigDescription) =
        general.Bind(
            section,
            key,
            value,
            description
        )
    let bindImitateVoice key (value: 'A) (description: 'B) = bind "Imitate voice" key value description
    let bindMaskedEnemy = bind "Masked enemy"
    let bindSpawnControl key (value: 'A) (description: 'B) = bind "Spawn control" key value description

    member val internal General = general
    member val internal Enemies = enemies

    member _.RegisterEnemy(enemyAI: EnemyAI) =
        try
            ignore <| enemies.Bind(
                "Enemies",
                enemyAI.enemyType.enemyName,
                enemyAI :? MaskedPlayerEnemy
            )
        with | _ -> logError $"Failed to register an enemy to the config: {enemyAI.GetType().Name}"


    member val MaskedMimicChance =
        let description = "Chance for masked enemy to start mimicking a player's suit, cosmetics, and voice."
        bindImitateVoice
            "Mimic chance (masked enemies)"
            100
            <| ConfigDescription(description, AcceptableValueRange(0, 100))

    member val NonMaskedMimicChance =
        let description = "Chance for non-masked enemies to start mimicking a player's voice."
        bindImitateVoice
            "Mimic chance (non-masked enemies)"
            100
            <| ConfigDescription(description, AcceptableValueRange(0, 100))

    member val MinimumDelayMasked =
        let description = "The minimum amount of time in between voice playbacks for masked enemies (in milliseconds)."
        bindImitateVoice
            "Minimum delay (masked enemy)"
            7000
            <| ConfigDescription(description, AcceptableValueRange(1, 600000))

    member val MaximumDelayMasked =
        let description = "The maximum amount of time in between voice playbacks for masked enemies (in milliseconds)."
        bindImitateVoice
            "Maximum delay (masked enemy)"
            12000
            <| ConfigDescription(description, AcceptableValueRange(1, 600000))

    member val MinimumDelayNonMasked =
        let description = "The minimum amount of time in between voice playbacks for non-masked enemies (in milliseconds)."
        bindImitateVoice
            "Minimum delay (non-masked enemies)"
            7000
            <| ConfigDescription(description, AcceptableValueRange(1, 600000))

    member val MaximumDelayNonMasked =
        let description = "The maximum amount of time in between voice playbacks for non-masked enemies (in milliseconds)."
        bindImitateVoice
            "Maximum delay (non-masked enemies)"
            12000
            <| ConfigDescription(description, AcceptableValueRange(1, 600000))
    
    member val EnableMimicVoiceWhileAlive =
        let description =
            "If true, players will always be able to hear monsters mimicking their voice.\n"
                + "If false, players will only be able to hear monsters mimicking their voice while the player is dead."
        bindImitateVoice
            "Enable the ability for players to hear enemies mimicking their voice while the player is alive."
            true
            <| ConfigDescription description
    
    member val EnableRecordVoiceWhileDead =
        let description =
            "If true, the microphone will always be recording during the round (after the lever is pulled).\n"
                + "If false, the microphone will only record while the player is alive (after the lever is pulled)."
        bindImitateVoice
            "Enable recording player voices while the player is dead."
            false
            <| ConfigDescription description

    member val EnableArmsOut =
        bindMaskedEnemy
            "Enable arms-out animation"
            false
            <| ConfigDescription "Whether the zombie arms animation should be used."
             

    member val EnableMaskTexture =
        bindMaskedEnemy
            "Enable mask texture"
            false
            <| ConfigDescription("Whether the masked enemy's mask texture should be shown.")
    
    member val EnableRadarSpin =
        bindMaskedEnemy
            "Enable radar spin"
            false
            <| ConfigDescription("Whether masked enemies should spin on the radar.")

    member val MimicVoiceWhileHiding =
        bindMaskedEnemy
            "Mimic voice while hiding"
            false
            <| ConfigDescription("Whether or not masked enemies should mimic voices while hiding on the ship")
    
    member val EnablePlayerNames =
        let description = "Whether or not name tags above a player should show. Useful for making it harder to distinguish masked enemies from players."
        bind
            "Player"
            "Enable player name tags"
            true
            <| ConfigDescription description
    
    member val EnableSpawnControl =
        let description =
            "If set to false, masked enemy spawns are untouched and are left at the vanilla spawn rates.\n"
                + "If set to true, masked enemy spawns will use the configured spawn chance."
        bindSpawnControl
            "Enable spawn control (masked enemies)"
            true
            <| ConfigDescription description

    member val MaskedSpawnChance =
        let description =
            "The percentage chance a masked enemy should naturally spawn. Spawn weights are internally calculated and modified to fit this percentage based on the moon.\n"
                + "Note: The spawn chance is based on each attempt the game tries to spawn an enemy. If you want a basically guaranteed spawn each round, set this to 25"
        bindSpawnControl
            "Masked enemy spawn chance"
            2.0f
            <| ConfigDescription(description, AcceptableValueRange(0.1f, 50.0f))

    member val MaxMaskedSpawns =
        let description =
            "The maximum number of masked enemies that can be naturally spawned within the same round.\n"
                + "Note: If this config option isn't working, it's often due to other mods overwriting the max spawns after Mirage sets it."
        bindSpawnControl
            "Max spawned masked enemies"
            2
            <| ConfigDescription description

let internal localConfig = LocalConfig(loadConfig "General", loadConfig "Enemies")

let internal getEnemyConfigEntries () =
    let mutable enemies = zero
    for key in localConfig.Enemies.Keys do
        try
            if localConfig.Enemies.ContainsKey key then
                let enemy = localConfig.Enemies[key] :?> ConfigEntry<bool>
                &enemies %= List.cons enemy
        with | error ->
            logError $"Failed to read entry from Mirage.Enemies.cfg:\n{error}"
    enemies

/// <summary>
/// Network synchronized configuration values. This is taken from the wiki:
/// https://lethal.wiki/dev/intermediate/custom-config-syncing
/// </summary>
[<Serializable>]
type SyncedConfig =
    {   /// Enemies that have voice mimicking enabled.
        enemies: Set<string>

        maskedMimicChance: int
        nonMaskedMimicChance: int
        
        minimumDelayMasked: int
        maximumDelayMasked: int
        minimumDelayNonMasked: int
        maximumDelayNonMasked: int

        enableMimicVoiceWhileAlive: bool
        enableRecordVoiceWhileDead: bool

        enableArmsOut: bool
        enableMaskTexture: bool
        enableRadarSpin: bool
        mimicVoiceWhileHiding: bool

        enablePlayerNames: bool
    }

let mutable private syncedConfig: Option<SyncedConfig> = None

let private toSyncedConfig () =
    let enemyKeys = localConfig.Enemies.Keys
    let mutable enemies = zero
    for key in enemyKeys do
        let mutable entry = null
        if localConfig.Enemies.TryGetEntry(key, &entry) && entry.Value then
            &enemies %= Set.add key.Key
    {   enemies = enemies

        maskedMimicChance = localConfig.MaskedMimicChance.Value
        nonMaskedMimicChance = localConfig.NonMaskedMimicChance.Value

        minimumDelayMasked = localConfig.MinimumDelayMasked.Value
        maximumDelayMasked = localConfig.MaximumDelayMasked.Value
        minimumDelayNonMasked = localConfig.MinimumDelayNonMasked.Value
        maximumDelayNonMasked = localConfig.MaximumDelayNonMasked.Value

        enableMimicVoiceWhileAlive = localConfig.EnableMimicVoiceWhileAlive.Value
        enableRecordVoiceWhileDead = localConfig.EnableRecordVoiceWhileDead.Value

        enableArmsOut = localConfig.EnableArmsOut.Value
        enableMaskTexture = localConfig.EnableMaskTexture.Value
        enableRadarSpin = localConfig.EnableRadarSpin.Value
        mimicVoiceWhileHiding = localConfig.MimicVoiceWhileHiding.Value

        enablePlayerNames = localConfig.EnablePlayerNames.Value
    }

let isConfigReady () = syncedConfig.IsSome

/// Get the currently synchronized config. This should only be used while in-game (not inside the menu).
let getConfig () =
    if not <| isConfigReady() then
        logWarning "syncedConfig has not been initialized yet."
    syncedConfig.Value

/// An action for synchronizing the <b>SyncedConfig</b>.
type internal SyncAction = RequestSync | ReceiveSync

/// Convert the action to the message event name.
let private toNamedMessage = function
    | RequestSync -> $"{pluginId}_OnRequestConfigSync"
    | ReceiveSync -> $"{pluginId}_OnReceiveConfigSync"

let private messageManager () = NetworkManager.Singleton.CustomMessagingManager
let private isClient () = NetworkManager.Singleton.IsClient
let private isHost () = NetworkManager.Singleton.IsHost

let private serializeToBytes<'A> (value: 'A) : array<byte> =
    let serializer = DataContractSerializer typeof<'A>
    use stream = new MemoryStream()
    try
        serializer.WriteObject(stream, value)
        stream.ToArray()
    with | error ->
        logError $"Failed to serialize value: {error}"
        null

let private deserializeFromBytes<'A> (data: array<byte>) : 'A =
    let serializer = DataContractSerializer typeof<'A>
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

let internal revertSync () = syncedConfig <- None

let internal requestSync () =
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
        Result.iterError logError <| monad' {
            if not <| reader.TryBeginRead sizeof<int> then
                return! Error "onReceiveSync failed while reading beginning of buffer."
            let mutable bytesLength = 0
            reader.ReadValueSafe &bytesLength
            if not <| reader.TryBeginRead(bytesLength) then
                return! Error "onReceiveSync failed. Host could not synchronize config."
            let bytes = Array.zeroCreate<byte> bytesLength
            reader.ReadBytesSafe(ref bytes, bytesLength)
            syncedConfig <- Some <| deserializeFromBytes bytes
        }

/// Register the named message handler for the given action.
let internal registerHandler action =
    let message = toNamedMessage action
    let register handler = messageManager().RegisterNamedMessageHandler(message, handler)
    let callback =
        match action with
            | RequestSync -> 
                syncedConfig <- Some <| toSyncedConfig()
                onRequestSync
            | ReceiveSync -> onReceiveSync
    register callback