module Mirage.Domain.Setting

#nowarn "40"

open FSharpPlus
open System
open System.IO
open System.Threading
open Newtonsoft.Json
open IcedTasks
open Mirage.Compatibility.Main
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Loop
open Mirage.Core.Task.Fork

/// Mirrors __Settings__ to represent the serializable format. Fields are nullable to make acting new fields seamless.
type SavedSettings =
    {   localPlayerVolume: Nullable<float32>
        neverDeleteRecordings: Nullable<bool>
        allowRecordVoice: Nullable<bool>
        muteVoiceMimic: Nullable<bool>
    }

let private defaultSettings =
    {   localPlayerVolume = Nullable 0.5f
        neverDeleteRecordings = Nullable false
        allowRecordVoice = Nullable true
        muteVoiceMimic = Nullable false
    }

type Settings =
    {   /// Volume used for voice play-back on monsters mimicking the local player. Must be a value between 0.0f-1.0f
        localPlayerVolume: float32
        /// If set to false, recordings are deleted when closing the game by default.
        neverDeleteRecordings: bool
        /// If set to false, recordings will not be created.
        allowRecordVoice: bool
        /// IF set to true, voice mimicking will not be played for the local player.
        muteVoiceMimic: bool
    }

let private fromSavedSettings savedSettings =
    let getValue (getField: SavedSettings -> Nullable<'A>) =
        Option.defaultValue (getField defaultSettings).Value <| Option.ofNullable (getField savedSettings)
    {   localPlayerVolume = getValue _.localPlayerVolume
        neverDeleteRecordings = getValue _.neverDeleteRecordings
        allowRecordVoice = getValue _.allowRecordVoice
        muteVoiceMimic = getValue _.muteVoiceMimic
    }

let mutable private settings = fromSavedSettings defaultSettings

let getSettings () = settings

let internal initSettings filePath pluginId pluginName pluginVersion =
    let channel = Channel CancellationToken.None
    let consumer () =
        forever <| fun () -> valueTask {
            let! settings = readChannel channel
            let text = JsonConvert.SerializeObject settings
            do! Async.AwaitTask(File.WriteAllTextAsync(filePath, text))
        }
    fork CancellationToken.None consumer

    let saveSettings updatedSettings =
        settings <- updatedSettings
        writeChannel channel updatedSettings

    valueTask {
        // Wait handle is used to allow only one process to initially write to the settings file.
        use handle = new EventWaitHandle(true, EventResetMode.AutoReset, pluginId)
        try
            ignore <| handle.WaitOne()
            let fileExists = File.Exists filePath
            let! savedSettings =
                if fileExists then 
                    JsonConvert.DeserializeObject<SavedSettings> <!> File.ReadAllTextAsync filePath
                else
                    result defaultSettings
            settings <- fromSavedSettings savedSettings
            if not fileExists then
                saveSettings settings
        finally
            ignore <| handle.Set()
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
                getMuteVoiceMimic = fun () -> settings.muteVoiceMimic
                setMuteVoiceMimic = fun value -> saveSettings { settings with muteVoiceMimic = value }
            }
        return settings
    }