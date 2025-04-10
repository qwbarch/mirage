module Mirage.Domain.Logger

open IcedTasks
open System.Threading
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Task.Loop

[<Struct>]
type private LogType = LogInfo | LogDebug | LogWarning | LogError

/// Logs messages in one thread to make it thread-safe.
let mutable private channel = None

let initLogger pluginId =
    channel <- Some <| Channel CancellationToken.None
    fork CancellationToken.None <| fun () ->
        let logger = BepInEx.Logging.Logger.CreateLogSource pluginId
        forever <| fun () -> valueTask {
            let! struct (logType, message) = readChannel channel.Value
            let logMessage =
                match logType with
                    | LogInfo -> logger.LogInfo
                    | LogDebug -> logger.LogDebug
                    | LogWarning -> logger.LogWarning
                    | LogError -> logger.LogError
            logMessage message
        }

let private logMessage logType (message: string) =
    ignore << writeChannel channel.Value <| struct (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError