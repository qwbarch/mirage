module Mirage.Domain.Setting

#nowarn "40"

open FSharpPlus
open FSharpx.Control
open System
open System.IO
open Newtonsoft.Json
open Mirage.PluginInfo
open Mirage.Compatibility

/// Mirrors __Settings__ to represent the serializable format. Fields are nullable to make acting new fields seamless.
type SavedSettings =
    {   localPlayerVolume: Nullable<float32>
        neverDeleteRecordings: Nullable<bool>
        allowRecordVoice: Nullable<bool>
    }

let private defaultSettings =
    {   localPlayerVolume = Nullable 0.5f
        neverDeleteRecordings = Nullable false
        allowRecordVoice = Nullable true
    }

type Settings =
    {   /// Volume used for voice play-back on monsters mimicking the local player. Must be a value between 0.0f-1.0f
        localPlayerVolume: float32
        /// If set to false, recordings are deleted when closing the game by default.
        neverDeleteRecordings: bool
        /// If set to false, recordings will not be created.
        allowRecordVoice: bool
    }

let private fromSavedSettings savedSettings =
    let getValue (getField: SavedSettings -> Nullable<'A>) =
        Option.defaultValue ((getField defaultSettings).Value) <| Option.ofNullable (getField savedSettings)
    {   localPlayerVolume = getValue _.localPlayerVolume
        neverDeleteRecordings = getValue _.neverDeleteRecordings
        allowRecordVoice = getValue _.allowRecordVoice
    }

let mutable private settings = fromSavedSettings defaultSettings

let getSettings () = settings

let internal initSettings filePath =
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
        let! savedSettings =
            Async.AwaitTask <|
                if File.Exists filePath then
                    JsonConvert.DeserializeObject<SavedSettings> <!> File.ReadAllTextAsync filePath
                else
                    result defaultSettings
        settings <- fromSavedSettings savedSettings
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