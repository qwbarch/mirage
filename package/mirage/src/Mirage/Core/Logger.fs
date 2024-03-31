/// A module providing logging functions that write to another thread.
module Mirage.Core.Logger

#nowarn "40"

open BepInEx
open System
open Mirage.PluginInfo
open Mirage.Core.Field

type private LogType = LogInfo | LogDebug | LogWarning | LogError

let private channel = new FSharpx.Control.BlockingQueueAgent<Tuple<LogType, string>>(Int32.MaxValue)

/// Run once on startup to allow log messages to be asynchronous dumped to a separate thread.
let internal initAsyncLogger () =
    let asyncLogger =
        async {
            let logger = Logging.Logger.CreateLogSource pluginId
            let rec consumer =
                async {
                    let! (logType, message) = channel.AsyncGet()
                    let logMessage =
                        match logType with
                            | LogInfo -> logger.LogInfo
                            | LogDebug -> logger.LogDebug
                            | LogWarning -> logger.LogWarning
                            | LogError -> logger.LogError
                    logMessage message
                    return! consumer
                }
            Async.StartImmediate consumer
        }
    Async.Start asyncLogger

let private logMessage logType message =
    Async.StartImmediate << channel.AsyncAdd <| (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError

/// Logs the error if an error message is found within the result, and runs the <b>onError</b> callback.
let logOnErrorWith (onError: Unit -> Unit) (program: Result<Unit, ErrorMessage>) =
    match program with
        | Ok () -> ()
        | Error message ->
            logError $"Tried to retrieve the value of a field while it contains nothing:\n{message()}"
            onError()

/// Logs the error if an error message is found within the result.
let logOnError : Result<Unit, ErrorMessage> -> Unit = logOnErrorWith id