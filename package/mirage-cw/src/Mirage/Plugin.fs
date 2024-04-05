namespace Mirage

open BepInEx
open Mirage.PluginInfo

[<BepInPlugin(pluginName, pluginId, pluginVersion)>]
type Plugin() =
    member this.Awake() = ()