module Mirage.Revive.Compatibility

open System
open PluginInfo
open BepInEx.Bootstrap
open LobbyCompatibility.Features
open LobbyCompatibility.Enums

let initLobbyCompatibility () =
    if Chainloader.PluginInfos.ContainsKey LobbyCompatibility.PluginInfo.PLUGIN_GUID then
        // Why Async? This is just to ensure the generated code forces PluginHelper.RegisterPlugin to be on a separate class,
        // which lets us use LobbyCompatibility as a soft dependency.
        Async.RunSynchronously <| async {
            PluginHelper.RegisterPlugin(
                pluginName,
                Version.Parse pluginVersion,
                CompatibilityLevel.Everyone,
                VersionStrictness.Minor
            )
        }