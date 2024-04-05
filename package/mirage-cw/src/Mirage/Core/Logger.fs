module Mirage.Core.Logger

#nowarn "40"

open System
open BepInEx
open FSharpx.Control
open Mirage.PluginInfo

type private LogType = LogInfo | LogDebug | LogWarning | LogError

let private logChannel = new BlockingQueueAgent<Tuple<LogType, string>>(Int32.MaxValue)

/// Run once on startup to allow log messages to be asynchronous dumped to a separate thread.
let internal initAsyncLogger () =
    let asyncLogger =
        async {
            let logger = Logging.Logger.CreateLogSource pluginId
            let rec consumer =
                async {
                    let! (logType, message) = logChannel.AsyncGet()
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
    Async.StartImmediate << logChannel.AsyncAdd <| (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError

/// <summary>
/// If the program results in an error, this function logs the error without rethrowing it.
/// </summary>
let internal handleResultWith (onError: Unit -> Unit) (program: Result<Unit, string>) : Unit =
    match program with
        | Ok _ -> ()
        | Result.Error message ->
            logError message
            onError()

/// <summary>
/// If the program results in an error, this function logs the error without rethrowing it.
/// </summary>
let internal handleResult : Result<Unit, string> -> Unit = handleResultWith id