namespace Mirage

open BepInEx
open System
open System.IO
open System.Reflection
open Dissonance
open UnityEngine
open Mirage.PluginInfo
open Mirage.Compatibility
open Mirage.Domain.Netcode
open Mirage.Domain.Setting
open Mirage.Domain.Config
open Mirage.Domain.Directory
open Mirage.Domain.Audio.Recording
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Prefab
open Mirage.Hook.Config
open Mirage.Hook.Microphone
open Mirage.Hook.Dissonance
open Mirage.Hook.MaskedPlayerEnemy

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(LethalSettings.GeneratedPluginInfo.Identifier, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LobbyCompatibility.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LethalConfig.PluginInfo.Guid, BepInDependency.DependencyFlags.SoftDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member _.Awake() =
        let assembly = Assembly.GetExecutingAssembly()
        ignore <| Directory.CreateDirectory mirageDirectory

        // Credits goes to DissonanceLagFix: https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/
        //for category in Seq.cast<LogCategory> <| Enum.GetValues typeof<LogCategory> do
        //    Logs.SetLogLevel(category, LogLevel.Error)

        initLethalConfig assembly localConfig.General
        initLobbyCompatibility pluginName pluginVersion
        initSettings <| Path.Join(mirageDirectory, "settings.json")
        initNetcodePatcher()
        Async.StartImmediate deleteRecordings
        Application.add_quitting(fun _ -> Async.StartImmediate deleteRecordings)

        // Hooks.
        cacheDissonance()
        disableAudioSpatializer()
        registerPrefab()
        syncConfig()
        readMicrophone recordingDirectory
        hookMaskedEnemy()