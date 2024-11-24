module Mirage.Compatibility

open BepInEx.Bootstrap

// Why are there async blocks with "Async.RunSynchronously"? This is just to ensure the generated code belongs in a separate class,
// which is required in order to use these dependencies as soft dependencies.

let initLethalConfig assembly configFile =
    if Chainloader.PluginInfos.ContainsKey LethalConfig.PluginInfo.Guid then
        Async.RunSynchronously <| async {
            LethalConfig.LethalConfigManager.LateConfigFiles.Add <|
            LethalConfig.AutoConfig.AutoConfigGenerator.ConfigFileAssemblyPair(
                ConfigFile = configFile,
                Assembly = assembly
            )
        }