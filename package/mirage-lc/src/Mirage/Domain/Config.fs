module Mirage.Domain.Config

open BepInEx
open FSharpPlus
open System
open System.IO
open System.Collections.Generic
open System.Runtime.Serialization
open BepInEx.Configuration
open Unity.Netcode
open Unity.Collections
open Newtonsoft.Json
open Mirage.Prelude
open Mirage.Domain.Logger

/// Default store items to enable in the config.
let defaultStoreItemWeight = function
    | "Walkie-talkie" -> 3
    | "Shovel" -> 15
    | "Pro-flashlight" -> 10
    | "Stun grenade" -> 1
    | "Extension ladder" -> 1
    | "Spray paint" -> 1
    | _ -> 0

/// Default scrap items to disable in the config.
let defaultDisabledScrapItems =
    Set.ofList
        [   "Apparatus"
        ]

/// Strip any non-supported characters by BepInEx as a config key.
let stripConfigKey = String.replace "'" ""

/// Max bytes of a packet. If the config payload is larger than this, it will be split into multiple packets.
let [<Literal>] MaxPacket = 1024

/// Serialized config is streamed into multiple packets if it exceeds __MaxPacket__.
/// This holds the packets until __FinishSync__ is found.
let mutable private receivedPackets = List<byte>()

let private loadConfig configName = ConfigFile(Path.Combine(Paths.ConfigPath, $"Mirage.{configName}.cfg"), true)

let [<Literal>] private StoreItemSection = "2. Store Item Weights"
let [<Literal>] private ScrapItemSection = "3. Scrap Item Blacklist (Enable to prevent masked from holding these items)"

type LocalConfig(general: ConfigFile, enemies: ConfigFile, items: ConfigFile) =
    let bind section key value (description: ConfigDescription) =
        general.Bind(
            section,
            key,
            value,
            description
        )
    let bindImitateVoice key (value: 'A) (description: 'B) = bind "Imitate voice" key value description
    let bindMaskedEnemy key (value: 'A) (description: 'B) = bind "Masked enemy" key value description
    let bindSpawnControl key (value: 'A) (description: 'B) = bind "Spawn control" key value description

    let bindConfigureItem key (value: 'A) (description: 'B) =
        items.Bind(
            "1. Masked held item configuration",
            key,
            value,
            description
        )

    member val internal General = general
    member val internal Enemies = enemies
    member val internal Items = items

    member _.RegisterEnemy(enemyAI: EnemyAI) =
        try
            ignore <| enemies.Bind(
                "Enemies",
                stripConfigKey enemyAI.enemyType.enemyName,
                enemyAI :? MaskedPlayerEnemy
            )
        with | _ -> logWarning $"Failed to register an enemy to the config: {enemyAI.GetType().Name}"
    
    member _.RegisterStoreItem(item: Item) =
        try
            ignore <| items.Bind(
                StoreItemSection,
                stripConfigKey item.itemName,
                defaultStoreItemWeight item.itemName 
            )
        with | _ -> logWarning $"Failed to register a store item to the config: {item.itemName}"
    
    member _.RegisterScrapItem(item: Item) =
        try
            ignore <| items.Bind(
                ScrapItemSection,
                stripConfigKey item.itemName,
                Set.contains item.itemName defaultDisabledScrapItems
            )
        with | _ -> logWarning $"Failed to register a scrap item to the config: {item.itemName}"

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
            <| ConfigDescription "Whether the masked enemy's mask texture should be shown."
    
    member val EnableRadarSpin =
        bindMaskedEnemy
            "Enable radar spin"
            false
            <| ConfigDescription "Whether masked enemies should spin on the radar."

    member val MimicVoiceWhileHiding =
        bindMaskedEnemy
            "Mimic voice while hiding"
            false
            <| ConfigDescription "Whether or not masked enemies should mimic voices while hiding on the ship"
    
    member val CopyMaskedVisuals =
        bindMaskedEnemy
            "Copy masked visuals"
            true
            <| ConfigDescription "Whether or not masked enemies should copy the player's visuals of who it's mimicking"
    
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
            0.5f
            <| ConfigDescription(description, AcceptableValueRange(0.1f, 50.0f))

    member val MaxMaskedSpawns =
        let description =
            "The maximum number of masked enemies that can be naturally spawned within the same round.\n"
                + "Note: If this config option isn't working, it's often due to other mods overwriting the max spawns after Mirage sets it."
        bindSpawnControl
            "Max spawned masked enemies"
            2
            <| ConfigDescription description
    
    member val MaskedItemSpawnChance =
        let description = "Percent chance for a masked enemy to spawn with an item. This is automatically disabled when LethalIntelligence is installed to allow it to control held items instead."
        bindConfigureItem
            "Chance to spawn with item"
            80
            <| ConfigDescription(description, AcceptableValueRange(0, 100))
    
    member val StoreItemRollChance =
        let description = "When a masked enemy spawns with an item, this config is the percent chance for the item to be a store item. When it fails the roll, it becomes a scrap item instead (using the current round's scrap weights)."
        bindConfigureItem
            "Chance to roll as store item"
            50
            <| ConfigDescription(description, AcceptableValueRange(0, 100))
    
    member val MaskedDropStoreItemOnDeath =
        let description = "Percent chance for a masked enemy to drop its held item on death (if it's a store item)."
        bindConfigureItem
            "Chance to drop held store item on death"
            0
            <| ConfigDescription(description, AcceptableValueRange(0, 100))
    
    member val MaskedDropScrapItemOnDeath =
        let description = "Percent chance for a masked enemy to drop its held item on death (if it's a scrap item)."
        bindConfigureItem
            "Chance to drop held scrap item on death"
            100
            <| ConfigDescription(description, AcceptableValueRange(0, 100))
    
    member val EmitFlashlightChance =
        let description = "Percent chance for flashlights to emit light while being held by a masked enemy."
        bindConfigureItem
            "Chance for flashlights to emit light"
            50
            <| ConfigDescription(description, AcceptableValueRange(0, 100))
    
    member val ScrapValueMultiplier =
        let description = "Value multiplier to use on scrap items that drop from masked enemies."
        bindConfigureItem 
            "Scrap value multiplier"
            1.0
            <| ConfigDescription description

let internal localConfig =
    LocalConfig(
        loadConfig "General",
        loadConfig "Enemies",
        loadConfig "Items"
    )

let internal getEnemyConfigEntries () =
    let mutable enemies = zero
    for key in localConfig.Enemies.Keys do
        try
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
    {   enemies: Set<string>
        totalItemWeights: int
        storeItemWeights: Map<string, int>
        disabledScrapItems: Set<string>

        maskedMimicChance: int
        nonMaskedMimicChance: int
        
        minimumDelayMasked: int
        maximumDelayMasked: int
        minimumDelayNonMasked: int
        maximumDelayNonMasked: int

        maskedItemSpawnChance: int
        storeItemRollChance: int
        maskedDropStoreItemOnDeath: int
        maskedDropScrapItemOnDeath: int
        emitFlashlightChance: int
        scrapValueMultiplier: float

        enableMimicVoiceWhileAlive: bool
        enableRecordVoiceWhileDead: bool

        enableArmsOut: bool
        enableMaskTexture: bool
        enableRadarSpin: bool
        mimicVoiceWhileHiding: bool
        copyMaskedVisuals: bool

        enablePlayerNames: bool
    }

let mutable private syncedConfig: Option<SyncedConfig> = None

let private toSyncedConfig () =
    // Load enemies.
    let mutable enemies = zero
    for key in localConfig.Enemies.Keys do
        let mutable enabled = null
        if localConfig.Enemies.TryGetEntry(key, &enabled) && enabled.Value then
            &enemies %= Set.add key.Key

    // Load items.
    let mutable storeItemWeights = zero
    let mutable disabledScrapItems = zero
    for key in localConfig.Items.Keys do
        match key.Section with
            | StoreItemSection ->
                let mutable weight = null
                if localConfig.Items.TryGetEntry(key, &weight) && weight.Value > 0 then
                    &storeItemWeights %= Map.add key.Key weight.Value
            | ScrapItemSection ->
                let mutable disabled = null
                if localConfig.Items.TryGetEntry(key, &disabled) && disabled.Value then
                    &disabledScrapItems %= Set.add key.Key
            | _ -> ()

    {   enemies = enemies
        totalItemWeights = sum <| Map.values storeItemWeights
        storeItemWeights = storeItemWeights
        disabledScrapItems = disabledScrapItems

        maskedMimicChance = localConfig.MaskedMimicChance.Value
        nonMaskedMimicChance = localConfig.NonMaskedMimicChance.Value

        minimumDelayMasked = localConfig.MinimumDelayMasked.Value
        maximumDelayMasked = localConfig.MaximumDelayMasked.Value
        minimumDelayNonMasked = localConfig.MinimumDelayNonMasked.Value
        maximumDelayNonMasked = localConfig.MaximumDelayNonMasked.Value

        maskedItemSpawnChance = localConfig.MaskedItemSpawnChance.Value
        storeItemRollChance = localConfig.StoreItemRollChance.Value
        maskedDropStoreItemOnDeath = localConfig.MaskedDropStoreItemOnDeath.Value
        maskedDropScrapItemOnDeath = localConfig.MaskedDropScrapItemOnDeath.Value
        emitFlashlightChance = localConfig.EmitFlashlightChance.Value
        scrapValueMultiplier = localConfig.ScrapValueMultiplier.Value

        enableMimicVoiceWhileAlive = localConfig.EnableMimicVoiceWhileAlive.Value
        enableRecordVoiceWhileDead = localConfig.EnableRecordVoiceWhileDead.Value

        enableArmsOut = localConfig.EnableArmsOut.Value
        enableMaskTexture = localConfig.EnableMaskTexture.Value
        enableRadarSpin = localConfig.EnableRadarSpin.Value
        mimicVoiceWhileHiding = localConfig.MimicVoiceWhileHiding.Value
        copyMaskedVisuals = localConfig.CopyMaskedVisuals.Value

        enablePlayerNames = localConfig.EnablePlayerNames.Value
    }

/// This should only be invoked by the host during the start of a new game.
let initSyncedConfig () =
    syncedConfig <- Some <| toSyncedConfig()

let isConfigReady () = syncedConfig.IsSome

/// Get the currently synchronized config. This should only be used while in-game (not inside the menu).
let getConfig () =
    if not <| isConfigReady() then
        logWarning "syncedConfig has not been initialized yet."
    syncedConfig.Value

/// An action for synchronizing the <b>SyncedConfig</b>.
type internal SyncAction
    = RequestSync
    | ReceiveSync
    | FinishSync

/// Convert the action to the message event name.
let messageId = "a5fb3c29-349d-4a27-a553-02ec39d58374"
let private toNamedMessage = function
    | RequestSync -> $"{messageId}_OnRequestConfigSync"
    | ReceiveSync -> $"{messageId}_OnReceiveConfigSync"
    | FinishSync -> $"{messageId}_OnFinishConfigSync"

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
    messageManager().SendNamedMessage(toNamedMessage action, clientId, stream, NetworkDelivery.ReliableSequenced)

let internal revertSync () = syncedConfig <- None

let internal requestSync () =
    if isClient() then
        use writer = new FastBufferWriter(sizeof<int32>, Allocator.Temp) 
        sendMessage RequestSync 0UL writer

let private onRequestSync clientId _ =
    if isHost() then
        let bytes = serializeToBytes <| getConfig()
        let playerId = StartOfRound.Instance.ClientPlayerList[clientId]
        logInfo $"Syncing configuration with Player #{playerId}"
        try
            for index in 0 .. (bytes.Length - 1) / MaxPacket do
                let offset = index * MaxPacket
                let packetLength = Math.Min(MaxPacket, bytes.Length - offset)
                use writer = new FastBufferWriter(packetLength + sizeof<int32>, Allocator.Temp)
                writer.WriteValueSafe &packetLength
                writer.WriteBytesSafe(bytes, packetLength, offset)
                sendMessage ReceiveSync clientId writer
        with | error ->
            logError $"Failed while sending ReceiveSync packet for Player #{playerId}: {error}"
        
        try
            use writer = new FastBufferWriter(sizeof<int32>, Allocator.Temp)
            sendMessage FinishSync clientId writer
        with | error ->
            logError $"Failed while sending FinishSync packet for Player #{playerId}: {error}"

let private onReceiveSync _ (reader: FastBufferReader) =
    if not <| isHost() then
        Result.iterError logError <| monad' {
            if not <| reader.TryBeginRead sizeof<int> then
                return! Error "onReceiveSync failed while reading beginning of buffer."
            let mutable packetLength = 0
            reader.ReadValueSafe &packetLength
            if not <| reader.TryBeginRead(packetLength) then
                return! Error "onReceiveSync failed while reading the packet length."
            let packet = Array.zeroCreate<byte> packetLength
            reader.ReadBytesSafe(ref packet, packetLength)
            receivedPackets.AddRange packet
        }

let onFinishSync _ _ =
    if not <| isHost() then
        try
            syncedConfig <- Some << deserializeFromBytes <| receivedPackets.ToArray()
            logInfo $"Received config from host: {JsonConvert.SerializeObject(syncedConfig.Value, Formatting.Indented)}"
        with | error ->
            logError $"Failed onFinishSync due to error: {error}"
        receivedPackets.Clear()

/// Register the named message handler for the given action.
let internal registerHandler action =
    let message = toNamedMessage action
    let register handler = messageManager().RegisterNamedMessageHandler(message, handler)
    let callback =
        match action with
            | RequestSync -> onRequestSync
            | ReceiveSync -> onReceiveSync
            | FinishSync -> onFinishSync
    register callback