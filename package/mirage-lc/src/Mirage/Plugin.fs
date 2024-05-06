namespace Mirage

open BepInEx
open System.IO
open NAudio.Lame
open Mirage.PluginInfo
open Mirage.Domain.Netcode
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.RecordAudio
open Mirage.Hook.RegisterPrefab
open Mirage.Hook.Dissonance

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        initNetcodePatcher()
        ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]

        // Hooks.
        registerPrefab()
        disableAudioSpatializer()
        recordAudio()
        fetchDissonance()