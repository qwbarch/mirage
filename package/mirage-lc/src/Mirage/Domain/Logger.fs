module Mirage.Domain.Logger

#nowarn "40"

open FSharpx.Control
open System
open Mirage.PluginInfo

type private LogType = LogInfo | LogDebug | LogWarning | LogError

let private channel = new BlockingQueueAgent<ValueTuple<LogType, string>>(Int32.MaxValue)

/// Run once on startup to allow log messages to be asynchronous dumped to a separate thread.
let internal initAsyncLogger () =
    Async.Start <| async {
        let logger = BepInEx.Logging.Logger.CreateLogSource pluginId
        let rec consumer =
            async {
                let! (logType, message) = channel .AsyncGet()
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

let private logMessage logType message =
    Async.StartImmediate << channel.AsyncAdd <| (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError