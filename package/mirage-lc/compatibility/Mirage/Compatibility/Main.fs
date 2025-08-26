module Mirage.Compatibility.Main

open System
open BepInEx.Bootstrap
open LobbyCompatibility.Features
open LobbyCompatibility.Enums
open LethalSettings.UI
open LethalSettings.UI.Components
open LethalConfig.ConfigItems
open Mirage.Compatibility.LethalSettings

let [<Literal>] LethalIntelligenceModId = "VirusTLNR.LethalIntelligence"

let inline isLethalIntelligenceLoaded () = Chainloader.PluginInfos.ContainsKey LethalIntelligenceModId

// Why are these not part of Mirage.dll?
// Due to the netcode patcher requiring a call to Assembly.GetExecutingAssembly().GetTypes(), it
// forces all classes to load, causing a missing dependency exception.
// Hence, these compatibility functions had to be separated to avoid loading them unless necessary.

// Why are these compatibility functions defined within an immediately invoked lazy block?
// They are compiled into separate classes in the underlying IL code, allowing it to not be executed
// if the dependency is missing.

let initGeneralLethalConfig assembly configFile =
    if Chainloader.PluginInfos.ContainsKey LethalConfig.PluginInfo.Guid then
        (lazy(
            LethalConfig.LethalConfigManager.CustomConfigFiles.Add <|
                LethalConfig.AutoConfig.AutoConfigGenerator.ConfigFileAssemblyPair(
                    ConfigFile = configFile,
                    Assembly = assembly
                )
        )).Force()

let mutable private enemiesInitialized = false
let initEnemiesLethalConfig assembly enemies =
    if Chainloader.PluginInfos.ContainsKey LethalConfig.PluginInfo.Guid && not enemiesInitialized then
        (lazy(
            enemiesInitialized <- not <| List.isEmpty enemies
            for enemy in enemies do
                let configItem = BoolCheckBoxConfigItem enemy
                ignore <| LethalConfig.LethalConfigManager.AddConfigItemForAssembly(configItem, assembly)
        )).Force()

let initLobbyCompatibility pluginName (pluginVersion: string) =
    if Chainloader.PluginInfos.ContainsKey LobbyCompatibility.PluginInfo.PLUGIN_GUID then
        (lazy(
            PluginHelper.RegisterPlugin(
                pluginName,
                Version.Parse pluginVersion,
                CompatibilityLevel.Everyone,
                VersionStrictness.Minor
            )
        )).Force()

type LethalSettingsArgs =
    {   pluginName: string
        pluginId: string
        pluginVersion: string
        getLocalPlayerVolume: unit -> float32
        setLocalPlayerVolume: float32 -> unit
        getNeverDeleteRecordings: unit -> bool
        setNeverDeleteRecordings: bool -> unit
        getAllowRecordVoice: unit -> bool
        setAllowRecordVoice: bool -> unit
        getMuteVoiceMimic: unit -> bool
        setMuteVoiceMimic: bool -> unit
    }

let initLethalSettings settings =
    if Chainloader.PluginInfos.ContainsKey LethalSettings.GeneratedPluginInfo.Identifier then
        (lazy(
            fixLethalSettings()
            ModMenu.RegisterMod(ModMenu.ModSettingsConfig(
                Name = settings.pluginName,
                Id = settings.pluginId,
                Version = settings.pluginVersion,
                Description = "The preferences below only affect yourself. These values are not synced from the host.",
                MenuComponents =
                    [|  SliderComponent(
                            Value = settings.getLocalPlayerVolume(),
                            MinValue = 0.0f,
                            MaxValue = 100.0f,
                            Text = "Volume when a monster is mimicking your own voice:",
                            OnValueChanged = fun _ value -> settings.setLocalPlayerVolume value
                        )
                        ToggleComponent(
                            Text = "Never delete recordings",
                            Value = settings.getNeverDeleteRecordings(),
                            OnValueChanged = fun _ value -> settings.setNeverDeleteRecordings value
                        )
                        ToggleComponent(
                            Text = "Allow record voice",
                            Value = settings.getAllowRecordVoice(),
                            OnValueChanged = fun _ value -> settings.setAllowRecordVoice value
                        )
                        ToggleComponent(
                            Text = "Mute voice mimic",
                            Value = settings.getMuteVoiceMimic(),
                            OnValueChanged = fun _ value -> settings.setMuteVoiceMimic value
                        )
                    |]
            ),
            true, // allowedInMainMenu
            true  // allowedInGame
            )
        )).Force()