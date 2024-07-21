namespace Mirage

open BepInEx
open System
open System.IO
open System.Diagnostics
open Silero.API
open Predictor.Lib
open NAudio.Lame
open Whisper.API
open UnityEngine
open FSharpPlus
open Mirage.PluginInfo
open Mirage.Core.Async.LVar
open Mirage.Domain.Netcode
open Mirage.Domain.Logger
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Microphone
open Mirage.Hook.RegisterPrefab
open Mirage.Hook.Dissonance
open Mirage.Hook.MaskedPlayerEnemy
open Mirage.Hook.Predictor
open Mirage.Unity.Recognition
open Mirage.Domain.Audio.Recording

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(StaticNetcodeLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    let [<Literal>] SamplesPerWindow = 1024

    member this.Awake() =
        Async.RunSynchronously <|
            async {
                initNetcodePatcher()
                let lameDllPath = Path.GetDirectoryName this.Info.Location
                let lameLoaded = LameDLL.LoadNativeDLL [|lameDllPath|]
                if not lameLoaded then
                    logError <|
                        "Failed to load NAudio.Lame. This means no monsters will be able to play your voice.\n"
                            + "Please report this to qwbarch at https://github.com/qwbarch/mirage/issues\n"
                            + $"Path failed: {lameDllPath}"
                let baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
                let mirageDirectory (name: string) =
                    let directory = Path.Join(baseDirectory, "Mirage")
                    Path.Join(directory, name)
                let recordingDirectory = mirageDirectory "Recording"
                let predictorDirectory = mirageDirectory "Predictor"

                logInfo "Initializing Whisper."
                let! (whisper, cudaAvailable) = startWhisper
                if cudaAvailable then
                    logInfo "CUDA is available. If you are hosting a game, your friends will be able to remotely use voice recognition via your gpu."
                else
                    logWarning
                        <| "CUDA is not available. Voice recognition will likely be incredibly slow for you.\n"
                        + "If the host of a game has CUDA available, they will be able to do the processing for you instead."

                logInfo "Initializing SileroVAD."
                let silero = SileroVAD SamplesPerWindow

                let toGuid (x: string) = new Guid(x)
                let! recordings =
                    getRecordings this.Info
                        |> map (map (Path.GetFileNameWithoutExtension >> toGuid) >> List.ofArray)
                logInfo $"Found {recordings.Length} recordings."
                do! initBehaviourPredictor
                        logInfo
                        logWarning
                        logError
                        predictorDirectory
                        recordings
                        Int32.MaxValue // Storage limit.
                        Int32.MaxValue // Memory limit.

                // Hooks.
                registerPrefab()
                disableAudioSpatializer()
                fetchDissonance()
                initMaskedEnemy()
                initPredictor()

                let sendRequest action =
                    async {
                        logInfo "remoteAction is running"
                        let! transcribers = readLVar RemoteTranscriber.Players
                        transcribers[StartOfRound.Instance.localPlayerController.playerClientId].SendRequest action
                    }

                let sendResponse playerId action =
                    async {
                        logInfo "sentenceAction is running"
                        let! transcribers = readLVar RemoteTranscriber.Players
                        transcribers[playerId].SendResponse action
                    }

                readMicrophone
                    {   recordingDirectory = recordingDirectory
                        cudaAvailable = cudaAvailable
                        whisper = whisper
                        silero = silero
                        isReady = newLVar false
                        transcribeViaHost = newLVar false // true // TODO: SET THIS VIA A CONFIG
                        sendRequest = sendRequest
                        sendResponse = sendResponse
                    }
            }