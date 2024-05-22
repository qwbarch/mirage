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
open Mirage.Hook.MaskedPlayerEnemy
open Mirage.Domain.Logger
open Mirage.Hook.VoiceRecognition

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(StaticNetcodeLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        initNetcodePatcher()
        ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]

        logInfo $"useCuda: {useCuda}"

        // Hooks.
        registerPrefab()
        disableAudioSpatializer()
        recordAudio()
        fetchDissonance()
        initMaskedEnemy()