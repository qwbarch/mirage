module Mirage.Hook.PreInitScene

open Dissonance
open BepInEx
open System
open System.IO
open System.Collections
open System.Threading
open System.Reflection
open UnityEngine
open Newtonsoft.Json
open IcedTasks
open Mirage.Core.Task.Fork
open Mirage.Compatibility
open Mirage.PluginInfo
open Mirage.Domain.Logger
open Mirage.Domain.Config
open Mirage.Domain.Directory
open Mirage.Domain.Netcode
open Mirage.Domain.Setting
open Mirage.Domain.Audio.Recording
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Prefab
open Mirage.Hook.Config
open Mirage.Hook.Microphone
open Mirage.Hook.MaskedPlayerEnemy
open Mirage.Hook.PlayerControllerB

let [<Literal>] private bundleName = "mapdotanimpack"

let hookPreInitScene (plugin: BaseUnityPlugin) =
    let main =
        seq {
            let assembly = Assembly.GetExecutingAssembly()
            let mutable assetBundle = null

            if isLethalIntelligenceLoaded() then
                assetBundle <-
                    AssetBundle.GetAllLoadedAssetBundles()
                        |> List.ofSeq
                        |> List.find (fun bundle -> bundle.name = bundleName)
            else
                let bundleRequest = AssetBundle.LoadFromFileAsync(Path.Combine(Path.GetDirectoryName plugin.Info.Location, bundleName))
                yield bundleRequest :> obj
                assetBundle <- bundleRequest.assetBundle

            if isNull assetBundle then
                raise <| InvalidProgramException $"Failed to load Mirage due to missing asset bundle: {bundleName}."

            let assetRequest = assetBundle.LoadAssetAsync<RuntimeAnimatorController> "MaskedMetarig.controller"
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
                let! settings = initSettings <| Path.Join(mirageDirectory, "settings.json")
                logInfo $"Loaded settings: {JsonConvert.SerializeObject(settings, Formatting.Indented)}"
                ignore <| deleteRecordings settings
                Application.add_quitting(ignore << deleteRecordings << getSettings)

                // Hooks.
                cacheDissonance()
                disableAudioSpatializer()
                registerPrefab()
                syncConfig()
                readMicrophone recordingDirectory
                hookMaskedEnemy maskedAnimatorController
                hookPlayerControllerB()
            }
        } :?> IEnumerator

    On.PreInitSceneScript.add_Start(fun orig self ->
        orig.Invoke self
        ignore <| self.StartCoroutine main
    )