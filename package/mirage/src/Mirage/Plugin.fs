namespace Mirage

open BepInEx
open Mirage.PluginInfo

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member _.Awake() = ()