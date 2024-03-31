namespace Mirage

open BepInEx
open FSharpPlus
open Mirage.PluginInfo
open Mirage.Core.Logger
open Mirage.Core.Field

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
type Plugin() =
    inherit BaseUnityPlugin()

    let (getCounter, counter) = useField()

    member _.Awake() =
        logOnError <| monad' {
            initAsyncLogger()
            let! counter = getCounter()
            logInfo $"counter is {counter}"
        }