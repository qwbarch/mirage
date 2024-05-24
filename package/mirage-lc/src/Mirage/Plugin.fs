namespace Mirage

open BepInEx
open System
open System.IO
open System.Diagnostics
open Predictor.Lib
open UnityEngine
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
        Async.RunSynchronously <|
            async {
                initNetcodePatcher()
                ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]

                logInfo "mirage awake"
                logInfo $"useCuda: {useCuda}"

                logInfo "before init behaviour predictor"
                logInfo $"guid: {guid}"
                let baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
                logInfo $"baseDirectory: {baseDirectory}"
                do! initBehaviourPredictor logInfo logWarning logError guid $"{baseDirectory}/Mirage/Predictor" Int32.MaxValue // Size limit
                logInfo "after init behaviour predictor"

                // Hooks.
                registerPrefab()
                disableAudioSpatializer()
                recordAudio()
                fetchDissonance()
                initMaskedEnemy()
            }