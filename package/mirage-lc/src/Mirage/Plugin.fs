namespace Mirage

open BepInEx
open Dissonance
open System
open System.IO
open System.Reflection
open System.Threading
open UnityEngine
open IcedTasks
open Newtonsoft.Json
open Mirage.PluginInfo
open Mirage.Compatibility
open Mirage.Core.Task.Fork
open Mirage.Domain.Netcode
open Mirage.Domain.Setting
open Mirage.Domain.Config
open Mirage.Domain.Directory
open Mirage.Domain.Audio.Recording
open Mirage.Domain.Logger
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
        fork CancellationToken.None <| fun () -> valueTask {
            let assembly = Assembly.GetExecutingAssembly()

            ignore <| Directory.CreateDirectory mirageDirectory
            let! settings = initSettings <| Path.Join(mirageDirectory, "settings.json")
            logInfo $"Loaded settings: {JsonConvert.SerializeObject settings}"

            // Credits goes to DissonanceLagFix: https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/
            for category in Seq.cast<LogCategory> <| Enum.GetValues typeof<LogCategory> do
                Logs.SetLogLevel(category, LogLevel.Error)

            initLobbyCompatibility pluginName pluginVersion
            initLethalConfig assembly localConfig.General
            initNetcodePatcher assembly
            ignore <| deleteRecordings()
            Application.add_quitting(ignore << deleteRecordings)

            // Hooks.
            cacheDissonance()
            disableAudioSpatializer()
            registerPrefab()
            syncConfig()
            readMicrophone recordingDirectory
            hookMaskedEnemy()
        }