namespace Mirage

open System
open System.IO
open BepInEx
open FSharpPlus
open HarmonyLib
open NAudio.Lame
open UnityEngine
open Mirage.Core.Audio.Recording
open Mirage.Core.Config
open Mirage.Core.Logger
open Mirage.PluginInfo
open Mirage.Patch.RegisterPrefab
open Mirage.Patch.RecordAudio
open Mirage.Patch.SyncConfig

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        handleResult <| monad' {
            initAsyncLogger()
            return! initConfig this.Config
            ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]
            deleteRecordings()
            Application.add_quitting deleteRecordings
            let harmony = new Harmony(pluginId)
            iter (unbox<Type> >> harmony.PatchAll)
                [   typeof<RegisterPrefab>
                    typeof<RecordAudio>
                    typeof<SyncConfig>
                ]
        }