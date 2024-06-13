namespace Mirage

open BepInEx
open System.IO
open System.Diagnostics
open Predictor.Lib
open NAudio.Lame
open Whisper.API
open Mirage.PluginInfo
open Mirage.Domain.Netcode
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Microphone
open Mirage.Hook.RegisterPrefab
open Mirage.Hook.Dissonance
open Mirage.Hook.MaskedPlayerEnemy
open Mirage.Domain.Logger
open Silero.API


[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(StaticNetcodeLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    let [<Literal>] SamplesPerWindow = 1024

    member this.Awake() =
        Async.RunSynchronously <|
            async {
                initNetcodePatcher()
                ignore <| LameDLL.LoadNativeDLL [|Path.GetDirectoryName this.Info.Location|]
                let baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
                let mirageDirectory (name: string) =
                    let directory = Path.Join(baseDirectory, "Mirage")
                    Path.Join(directory, name)
                let recordingDirectory = mirageDirectory "Recording"
                let predictorDirectory = mirageDirectory "Predictor"

                logInfo $"recording directory: {recordingDirectory}"

                logInfo "Initializing Whisper."
                let! (whisper, cudaAvailable) = startWhisper
                logInfo $"Cuda available: {cudaAvailable}"

                logInfo "Initializing SileroVAD."
                let silero = SileroVAD SamplesPerWindow

                //do! initBehaviourPredictor logInfo logWarning logError guid $"{baseDirectory}/Mirage/Predictor" Int32.MaxValue // Size limit

                // TODO: Do this properly.
                let rec keepActive =
                    async {
                        userIsActivePing()
                        do! Async.Sleep 5000
                        do! keepActive
                    }
                Async.Start keepActive

                // Hooks.
                registerPrefab()
                disableAudioSpatializer()
                readMicrophone whisper silero recordingDirectory
                fetchDissonance()
                initMaskedEnemy()
            }