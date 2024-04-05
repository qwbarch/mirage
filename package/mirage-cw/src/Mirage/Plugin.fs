namespace Mirage

open BepInEx
open Mirage.PluginInfo

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    member _.Awake() = ()