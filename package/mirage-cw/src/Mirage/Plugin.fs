namespace Mirage

open System
open System.IO
open BepInEx
open FSharpPlus
open HarmonyLib
open NAudio.Lame
open Mirage.PluginInfo
open Mirage.Core.Logger
open Mirage.Patch.RegisterPrefab
open Mirage.Patch.RecordAudio

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        initAsyncLogger()
        ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]
        let harmony = new Harmony(pluginId)
        iter (unbox<Type> >> harmony.PatchAll)
            [   typeof<RegisterPrefab>
                typeof<RecordAudio>
            ]