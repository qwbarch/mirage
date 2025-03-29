namespace Mirage

open BepInEx
open Dissonance
open System
open System.IO
open System.Collections
open System.Threading
open System.Reflection
open UnityEngine
open Newtonsoft.Json
open IcedTasks
open Mirage.PluginInfo
open Mirage.Hook.PreInitScene
open Mirage.Compatibility
open Mirage.Core.Task.Fork
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
open Mirage.Hook.Dissonance
open Mirage.Hook.Item


[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(LethalSettings.GeneratedPluginInfo.Identifier, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LobbyCompatibility.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LethalConfig.PluginInfo.Guid, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LethalIntelligenceModId, BepInDependency.DependencyFlags.SoftDependency)>]
type Plugin() as self =
    inherit BaseUnityPlugin()

    let [<Literal>] bundleName = "mapdotanimpack"

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
                let bundleRequest = AssetBundle.LoadFromFileAsync(Path.Combine(Path.GetDirectoryName self.Info.Location, bundleName))
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
                populateItems()
                disableAudioSpatializer()
                registerPrefab()
                syncConfig()
                readMicrophone recordingDirectory
                hookMaskedEnemy maskedAnimatorController
                hookPlayerControllerB()
            }
        } :?> IEnumerator

    member _.Awake() = hookPreInitScene main