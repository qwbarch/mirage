module Mirage.Compatibility

open BepInEx.Bootstrap
open System
open LobbyCompatibility.Features
open LobbyCompatibility.Enums

// Why are these not part of Mirage.dll?
// Due to the netcode patcher requiring a call to Assembly.GetExecutingAssembly().GetTypes(), it
// loads forces all classes to load, causing a missing dependency exception.
// Hence, these compatibility functions had to be separated to avoid loading them unless necessary.

// Why are these compatibility functions declined within a closure?
// With optimize set to false in the .fsproj, these are compiled into a separate class,
// which is needed when using these dependencies as soft dependencies.

let initLethalConfig assembly configFile =
    if Chainloader.PluginInfos.ContainsKey LethalConfig.PluginInfo.Guid then
        let run () =
            LethalConfig.LethalConfigManager.LateConfigFiles.Add <|
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