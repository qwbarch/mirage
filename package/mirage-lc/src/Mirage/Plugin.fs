namespace Mirage

open BepInEx
open System.IO
open NAudio.Lame
open Mirage.PluginInfo
open Mirage.Domain.Netcode
open Mirage.Hook.AudioSpatializer
open Mirage.Domain.Logger

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member this.Awake() =
        let lameDllPath = Path.GetDirectoryName this.Info.Location
        let lameLoaded = LameDLL.LoadNativeDLL [|lameDllPath|]
        if not lameLoaded then
            logError <|
                "Failed to load NAudio.Lame. This means no monsters will be able to play your voice.\n"
                    + "Please report this to qwbarch at https://github.com/qwbarch/mirage/issues\n"
                    + $"Path failed: {lameDllPath}"
        initNetcodePatcher()
        disableAudioSpatializer()