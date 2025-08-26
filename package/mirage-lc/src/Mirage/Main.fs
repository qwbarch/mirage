module Mirage.Main

open Dissonance
open System
open System.IO
open System.Collections
open System.Threading
open System.Reflection
open UnityEngine
open Newtonsoft.Json
open IcedTasks
open Mirage.Compatibility.Main
open Mirage.Core.Task.Fork
open Mirage.Domain.Logger
open Mirage.Domain.Config
open Mirage.Domain.Directory
open Mirage.Domain.Netcode
open Mirage.Domain.Setting
open Mirage.Domain.Audio.Recording
open Mirage.Domain.Null
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Prefab
open Mirage.Hook.Config
open Mirage.Hook.Microphone
open Mirage.Hook.MaskedPlayerEnemy
open Mirage.Hook.PlayerControllerB
open Mirage.Hook.Dissonance
open Mirage.Hook.Item

let [<Literal>] bundleName = "Mirage.unity3d"

let main pluginLocation pluginId pluginName pluginVersion =
    seq {
        initLogger pluginId
        let assembly = Assembly.GetExecutingAssembly()

        let bundleRequest = AssetBundle.LoadFromFileAsync(Path.Combine(pluginLocation, bundleName))
        yield bundleRequest :> obj
        let assetBundle = bundleRequest.assetBundle

        if isNull assetBundle then
            raise <| InvalidProgramException $"Failed to load Mirage due to missing asset bundle: {bundleName}."

        let assetRequest = assetBundle.LoadAssetAsync<RuntimeAnimatorController> "metarig_0.controller"
        yield assetRequest :> obj
        let maskedAnimatorController = assetRequest.asset :?> RuntimeAnimatorController

        // Credits goes to DissonanceLagFix: https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/
        for category in Seq.cast<LogCategory> <| Enum.GetValues typeof<LogCategory> do
            Logs.SetLogLevel(category, LogLevel.Error)

        fork CancellationToken.None <| fun () -> valueTask {
            initLobbyCompatibility pluginName pluginVersion
            initGeneralLethalConfig assembly localConfig.General

            initNetcodePatcher assembly

            ignore <| Directory.CreateDirectory mirageDirectory
            let! settings =
                initSettings
                    (Path.Join(mirageDirectory, "settings.json"))
                    pluginId
                    pluginName
                    pluginVersion
            logInfo $"Loaded settings: {JsonConvert.SerializeObject(settings, Formatting.Indented)}"
            ignore <| deleteRecordings settings
            Application.add_quitting(ignore << deleteRecordings << getSettings)

            // Hooks.
            cacheDissonance()
            readMicrophone recordingDirectory
            populateItems()
            disableAudioSpatializer()
            registerPrefab()
            syncConfig()
            hookMaskedEnemy maskedAnimatorController
            hookPlayerControllerB()
        }
    } :?> IEnumerator