module Mirage.Domain.Logger

#nowarn "40"

open System
open FSharpx.Control
open Mirage.PluginInfo

type private LogType = LogInfo | LogDebug | LogWarning | LogError

/// Logs messages in one thread to make it thread-safe.
let private channel =
    let self = new BlockingQueueAgent<ValueTuple<LogType, string>>(Int32.MaxValue)
    Async.Start <| async {
        let logger = BepInEx.Logging.Logger.CreateLogSource pluginId
        let rec consumer =
            async {
                let! (logType, message) = self.AsyncGet()
                let logMessage =
                    match logType with
                        | LogInfo -> logger.LogInfo
                        | LogDebug -> logger.LogDebug
                        | LogWarning -> logger.LogWarning
                        | LogError -> logger.LogError
                logMessage message
                do! consumer
            }
        Async.StartImmediate consumer
    }
    self

let private logMessage logType message =
    Async.StartImmediate << channel.AsyncAdd <| (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError