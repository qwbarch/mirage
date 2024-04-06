namespace Mirage

open System
open BepInEx
open FSharpPlus
open HarmonyLib
open Mirage.PluginInfo
open Mirage.Core.Logger
open Mirage.Patch.RegisterPrefab

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member _.Awake() =
        initAsyncLogger()
        let harmony = new Harmony(pluginId)
        iter (unbox<Type> >> harmony.PatchAll)
            [   typeof<RegisterPrefab>
            ]