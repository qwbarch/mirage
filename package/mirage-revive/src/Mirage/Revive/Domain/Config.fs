module Mirage.Revive.Domain.Config

open FSharpPlus
open BepInEx.Configuration
open Unity.Collections
open Unity.Netcode
open System
open System.IO
open System.Runtime.Serialization
open Mirage.Revive.PluginInfo
open Mirage.Revive.Domain.Logger

type LocalConfig(config: ConfigFile) =
    let [<Literal>] section = "Revive"

    member val ReviveChance =
        let description = "The chance a player will revive as a masked enemy, on player death."
        config.Bind<int>(
            section,
            "Revive chance",
            10,
            ConfigDescription(description, AcceptableValueRange(0, 100))
        )
    member val ReviveOnlyWhenPlayerAlone =
        let description =
            "If enabled, revivals only happen when the player dies alone (where others will almost never see the masked enemy come to life from your body)."
                + "If disabled, revivals will always happen based on the configured chance, which means players that are near you can see the masked enemy coming back to life."
        config.Bind<bool>(
            section,
            "Revive only when alone",
            false,
            description
        )

/// <summary>
/// Network synchronized configuration values. This is taken from the wiki:
/// https://lethal.wiki/dev/intermediate/custom-config-syncing
/// </summary>
[<Serializable>]
type SyncedConfig =
    {   reviveChance: int
        reviveOnlyWhenPlayerAlone: bool
    }

let mutable private localConfig: Option<LocalConfig> = None
let mutable private syncedConfig: Option<SyncedConfig> = None

let private toSyncedConfig () =
    {   reviveChance = localConfig.Value.ReviveChance.Value
        reviveOnlyWhenPlayerAlone = localConfig.Value.ReviveOnlyWhenPlayerAlone.Value
    }

type SyncAction = RequestSync | ReceiveSync

let private toNamedMessage = function
    | RequestSync -> $"{pluginId}_OnRequestConfigSync"
    | ReceiveSync -> $"{pluginId}_OnReceiveConfigSync"

let private messageManager () = NetworkManager.Singleton.CustomMessagingManager
let private isClient () = NetworkManager.Singleton.IsClient
let private isHost () = NetworkManager.Singleton.IsHost

let internal initConfig configFile = localConfig <- Some <| LocalConfig configFile

let getConfig () = Option.defaultWith (konst <| toSyncedConfig()) syncedConfig

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