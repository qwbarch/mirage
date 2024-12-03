module Mirage.Domain.Logger

open FSharp.Control.Tasks.Affine.Unsafe
open Mirage.PluginInfo
open Mirage.Core.Ply.Fork
open Mirage.Core.Ply.Channel

type private LogType = LogInfo | LogDebug | LogWarning | LogError

/// Logs messages in one thread to make it thread-safe.
let private channel =
    let self = Channel()
    fork' <| fun () ->
        let logger = BepInEx.Logging.Logger.CreateLogSource pluginId
        let rec consumer () =
            uply {
                let! struct (logType, message) = readChannel' self
                let logMessage =
                    match logType with
                        | LogInfo -> logger.LogInfo
                        | LogDebug -> logger.LogDebug
                        | LogWarning -> logger.LogWarning
                        | LogError -> logger.LogError
                logMessage message
                do! consumer()
            }
        consumer()
    self

let private logMessage logType (message: string) =
    writeChannel channel <| struct (logType, message)

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError