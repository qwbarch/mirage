namespace Mirage.Revive

open BepInEx
open Mirage.Revive.PluginInfo
open Mirage.Revive.Compatibility
open Mirage.Revive.Domain.Netcode
open Mirage.Revive.Domain.Config
open Mirage.Revive.Hook.Config
open Mirage.Revive.Hook.MaskedPlayerEnemy

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(LobbyCompatibility.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        initConfig this.Config
        initNetcodePatcher()
        initLobbyCompatibility()

        // Hooks.
        syncConfig()
        revivePlayersOnDeath()