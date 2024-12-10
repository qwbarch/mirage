module Mirage.Revive.Domain.Logger

open Mirage.Revive.PluginInfo

[<Struct>]
type private LogType = LogInfo | LogDebug | LogWarning | LogError

let private logger = BepInEx.Logging.Logger.CreateLogSource pluginId

let private logMessage logType : string -> unit =
    match logType with
        | LogInfo -> logger.LogInfo
        | LogDebug -> logger.LogDebug
        | LogWarning -> logger.LogWarning
        | LogError -> logger.LogError

let internal logInfo = logMessage LogInfo
let internal logDebug = logMessage LogDebug
let internal logWarning = logMessage LogWarning
let internal logError = logMessage LogError