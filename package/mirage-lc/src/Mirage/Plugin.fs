namespace Mirage

open BepInEx
open Mirage.PluginInfo
open Mirage.Hook.PreInitScene
open Mirage.Compatibility

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(LethalSettings.GeneratedPluginInfo.Identifier, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LobbyCompatibility.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LethalConfig.PluginInfo.Guid, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(lethalIntelligenceModId, BepInDependency.DependencyFlags.SoftDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() = hookPreInitScene this