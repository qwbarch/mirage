module Mirage.Domain.Setting

#nowarn "40"

open FSharpPlus
open FSharpx.Control
open LethalSettings.UI
open LethalSettings.UI.Components
open System
open System.IO
open Newtonsoft.Json
open Mirage.PluginInfo

type Settings =
    {   /// Volume used for voice play-back on monsters mimicking the local player. Must be a value between 0.0f-1.0f
        localPlayerVolume: float32
        /// Whether monsters mimicking the local player should be muted while the player is alive.
        hearLocalVoiceWhileAlive: bool
        /// Whether the microphone should continue recording while the local player is dead.
        recordWhileDead: bool
        /// If set to false, recordings are deleted when closing the game by default.
        neverDeleteRecordings: bool
    }

let defaultSettings =
    {   localPlayerVolume = 1.0f
        hearLocalVoiceWhileAlive = true
        recordWhileDead = true
        neverDeleteRecordings = false
    }

let mutable private settings = defaultSettings

let getSettings () = settings

let initSettings filePath =
    let channel =
        let agent = new BlockingQueueAgent<Settings>(Int32.MaxValue)
        let rec consumer =
            async {
                let! settings = agent.AsyncGet()
                let text = JsonConvert.SerializeObject settings
                do! Async.AwaitTask(File.WriteAllTextAsync(filePath, text))
                do! consumer
            }
        Async.Start consumer
        agent
    let saveSettings updatedSettings =
        settings <- updatedSettings
        channel.Add updatedSettings
    Async.StartImmediate <| async {
        let! previousSettings =
            Async.AwaitTask <|
                if File.Exists filePath then
                    JsonConvert.DeserializeObject<Settings> <!> File.ReadAllTextAsync filePath
                else
                    result defaultSettings
        settings <- previousSettings
        ModMenu.RegisterMod(ModMenu.ModSettingsConfig(
            Name = pluginName,
            Id = pluginId,
            Version = pluginVersion,
            Description = "The preferences below only affect yourself. Each player can choose what they prefer.",
            MenuComponents =
                [|  SliderComponent(
                        Value = settings.localPlayerVolume * 100.0f,
                        MinValue = 0.0f,
                        MaxValue = 100.0f,
                        Text = "Volume when a monster is mimicking your own voice:",
                        OnValueChanged = fun _ value -> saveSettings { settings with localPlayerVolume = value / 100.0f }
                    )
                    ToggleComponent(
                        Text = "Only hear a monster mimicking your own voice while spectating",
                        Value = not settings.hearLocalVoiceWhileAlive,
                        OnValueChanged = fun _ value -> saveSettings { settings with hearLocalVoiceWhileAlive = not value }
                    )
                    ToggleComponent(
                        Text = "Only record your voice while alive",
                        Value = not settings.recordWhileDead,
                        OnValueChanged = fun _ value -> saveSettings { settings with recordWhileDead = not value }
                    )
                    ToggleComponent(
                        Text = "Never delete recordings",
                        Value = settings.neverDeleteRecordings,
                        OnValueChanged = fun _ value -> saveSettings { settings with neverDeleteRecordings = value }
                    )
                |]
        ),
        true, // allowedInMainMenu
        true  // allowedInGame
        )
    }