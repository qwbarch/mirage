module Mirage.Compatibility

open BepInEx.Bootstrap
open System
open LobbyCompatibility.Features
open LobbyCompatibility.Enums
open LethalSettings.UI
open LethalSettings.UI.Components
open LethalConfig.ConfigItems
open LethalConfig.ConfigItems.Options

// Why are these not part of Mirage.dll?
// Due to the netcode patcher requiring a call to Assembly.GetExecutingAssembly().GetTypes(), it
// forces all classes to load, causing a missing dependency exception.
// Hence, these compatibility functions had to be separated to avoid loading them unless necessary.

// Why are these compatibility functions defined within a closure?
// With optimize set to false in the .fsproj, these are compiled into a separate class,
// which is needed when using these dependencies as soft dependencies.

let initLethalConfig assembly configFile =
    if Chainloader.PluginInfos.ContainsKey LethalConfig.PluginInfo.Guid then
        let run () =
            LethalConfig.LethalConfigManager.CustomConfigFiles.Add <|
                LethalConfig.AutoConfig.AutoConfigGenerator.ConfigFileAssemblyPair(
                    ConfigFile = configFile,
                    Assembly = assembly
                )
        run()

let initLobbyCompatibility pluginName (pluginVersion: string) =
    if Chainloader.PluginInfos.ContainsKey LobbyCompatibility.PluginInfo.PLUGIN_GUID then
        let run () =
            PluginHelper.RegisterPlugin(
                pluginName,
                Version.Parse pluginVersion,
                CompatibilityLevel.Everyone,
                VersionStrictness.Minor
            )
        run()

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
    }

let initLethalSettings settings =
    if Chainloader.PluginInfos.ContainsKey LethalSettings.GeneratedPluginInfo.Identifier then
        let run () =
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
                    |]
            ),
            true, // allowedInMainMenu
            true  // allowedInGame
            )
        run()