module Mirage.Dependency.LethalConfig

open BepInEx.Bootstrap
open System.Reflection
open Mirage.Domain.Config

let internal initLethalConfig () =
    if Chainloader.PluginInfos.ContainsKey "ainavt.lc.lethalconfig" then
        // Why Async? This is just to ensure the generated code forces PluginHelper.RegisterPlugin to be on a separate class,
        // which lets us use LobbyCompatibility as a soft dependency.
        Async.RunSynchronously <| async {
            let assembly = Assembly.GetExecutingAssembly()
            LethalConfig.LethalConfigManager.LateConfigFiles.Add <|
                LethalConfig.AutoConfig.AutoConfigGenerator.ConfigFileAssemblyPair(
                    ConfigFile = localConfig.General,
                    Assembly = assembly
                )
        }