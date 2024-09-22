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

let private mkConfigFile configName = ConfigFile(Path.Combine(Paths.ConfigPath, $"Mirage.{configName}.cfg"), true)

type LocalConfig(general: ConfigFile, enemies: ConfigFile) =
    let bind section key value (description: string) =
        general.Bind(
            section,
            key,
            value,
            description
        )
    let bindImitateVoice = bind "Imitate voice"

    member val internal General = general
    member val internal Enemies = enemies

    member _.RegisterEnemy(enemyAI: EnemyAI) =
        ignore <| enemies.Bind(
            "Imitate voice",
            enemyAI.enemyType.enemyName,
            enemyAI :? MaskedPlayerEnemy
        )

    member val MinimumDelayMasked =
        bindImitateVoice
            "Minimum delay (masked enemy)"
            7000
            "The minimum amount of time in between voice playbacks for masked enemies (in milliseconds)."

    member val MaximumDelayMasked =
        bindImitateVoice
            "Maximum delay (masked enemy)"
            12000
            "The maximum amount of time in between voice playbacks for masked enemies (in milliseconds)."

    member val MinimumDelayNonMasked =
        bindImitateVoice
            "Minimum delay (non-masked enemies)"
            7000
            "The minimum amount of time in between voice playbacks for non-masked enemies (in milliseconds)."

    member val MaximumDelayNonMasked =
        bindImitateVoice
            "Maximum delay (non-masked enemies)"
            12000
            "The maximum amount of time in between voice playbacks for non-masked enemies (in milliseconds)."
    
let localConfig = LocalConfig(mkConfigFile "General", mkConfigFile "Enemies")

/// <summary>
/// Network synchronized configuration values. This is taken from the wiki:
/// https://lethal.wiki/dev/intermediate/custom-config-syncing
/// </summary>
[<Serializable>]
type SyncedConfig =
    {   /// Enemies that have voice mimicking enabled.
        enemies: Set<string>

        minimumDelayMasked: int
        maximumDelayMasked: int
        minimumDelayNonMasked: int
        maximumDelayNonMasked: int
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
        minimumDelayMasked = localConfig.MinimumDelayMasked.Value
        maximumDelayMasked = localConfig.MaximumDelayMasked.Value
        minimumDelayNonMasked = localConfig.MinimumDelayMasked.Value
        maximumDelayNonMasked = localConfig.MaximumDelayMasked.Value
    }

/// Get the currently synchronized config. This should only be used while in-game (not inside the menu).
let getConfig () = syncedConfig.Value

/// An action for synchronizing the <b>SyncedConfig</b>.
type internal SyncAction = RequestSync | ReceiveSync

/// Convert the action to the message event name.
let private toNamedMessage (action: SyncAction) =
    match action with
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