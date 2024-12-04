module Mirage.Domain.Logger

open IcedTasks
open System.Threading
open Mirage.PluginInfo
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Task.Utility

[<Struct>]
type private LogType = LogInfo | LogDebug | LogWarning | LogError

/// Logs messages in one thread to make it thread-safe.
let private channel =
    let self = Channel CancellationToken.None
    fork CancellationToken.None <| fun () ->
        let logger = BepInEx.Logging.Logger.CreateLogSource pluginId
        forever <| fun () -> valueTask {
            let! struct (logType, message) = readChannel self
            let logMessage =
                match logType with
                    | LogInfo -> logger.LogInfo
                    | LogDebug -> logger.LogDebug
                    | LogWarning -> logger.LogWarning
                    | LogError -> logger.LogError
            logMessage message
        }
    self

let private logMessage logType (message: string) =
    ignore << writeChannel channel <| struct (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError