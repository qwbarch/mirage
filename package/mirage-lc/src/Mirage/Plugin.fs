namespace Mirage

open BepInEx
open Dissonance
open System
open System.IO
open System.Diagnostics
open NAudio.Lame
open UnityEngine
open Mirage.PluginInfo
open Mirage.Domain.Netcode
open Mirage.Domain.Logger
open Mirage.Domain.Setting
open Mirage.Domain.Audio.Recording
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Prefab
open Mirage.Hook.Config
open Mirage.Hook.Microphone
open Mirage.Hook.Dissonance
open Mirage.Hook.MaskedPlayerEnemy

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        let lameDllPath = Path.GetDirectoryName this.Info.Location
        let lameLoaded = LameDLL.LoadNativeDLL [|lameDllPath|]
        let baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
        let mirageDirectory = Path.Join(baseDirectory, "Mirage")
        let recordingDirectory = Path.Join(mirageDirectory, "Recording")
        ignore <| Directory.CreateDirectory mirageDirectory
        if not lameLoaded then
            logError <|
                "Failed to load NAudio.Lame. This means no monsters will be able to play your voice.\n"
                    + "Please report this to qwbarch at https://github.com/qwbarch/mirage/issues\n"
                    + $"Path failed: {lameDllPath}"

        // Credits goes to DissonanceLagFix: https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/
        for category in Seq.cast<LogCategory> <| Enum.GetValues typeof<LogCategory> do
            Logs.SetLogLevel(category, LogLevel.Error)

        initRecordingManager recordingDirectory
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