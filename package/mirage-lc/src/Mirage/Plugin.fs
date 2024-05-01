namespace Mirage

open BepInEx
open System.IO
open NAudio.Lame
open Mirage.PluginInfo
open Mirage.Domain.Logger
open Mirage.Hook.RecordAudio
open Mirage.Hook.AudioSpatializer

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        initAsyncLogger()
        ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]

        // Hooks.
        disableAudioSpatializer()
        recordAudio()