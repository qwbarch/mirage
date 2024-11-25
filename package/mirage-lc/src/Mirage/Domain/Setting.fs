module Mirage.Domain.Setting

#nowarn "40"

open FSharpPlus
open FSharpx.Control
open System
open System.IO
open Newtonsoft.Json
open Mirage.PluginInfo
open Mirage.Compatibility

type Settings =
    {   /// Volume used for voice play-back on monsters mimicking the local player. Must be a value between 0.0f-1.0f
        localPlayerVolume: float32
        /// If set to false, recordings are deleted when closing the game by default.
        neverDeleteRecordings: bool
        /// If set to false, recordings will not be created.
        allowRecordVoice: bool
    }

let defaultSettings =
    {   localPlayerVolume = 0.5f
        neverDeleteRecordings = false
        allowRecordVoice = true
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
        initLethalSettings
            {   pluginId = pluginId
                pluginVersion = pluginVersion
                pluginName = pluginName
                getLocalPlayerVolume = fun () -> settings.localPlayerVolume * 100.0f
                setLocalPlayerVolume = fun value -> saveSettings { settings with localPlayerVolume = value / 100.0f }
                getNeverDeleteRecordings = fun () -> settings.neverDeleteRecordings
                setNeverDeleteRecordings = fun value -> saveSettings { settings with neverDeleteRecordings = value }
                getAllowRecordVoice = fun () -> settings.allowRecordVoice
                setAllowRecordVoice = fun value -> saveSettings { settings with allowRecordVoice = value }
            }
    }