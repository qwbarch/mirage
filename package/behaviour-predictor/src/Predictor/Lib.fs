module Predictor.Lib

open System
open System.IO
open Predictor.Domain
open Predictor.MimicPool
open Predictor.Learner
open Predictor.PolicyFileHandler
open Predictor.ObservationGenerator
open Predictor.Model
open Embedding
open Mirage.Core.Async.LVar
open Mirage.Core.Async.MVar
open System.Reflection
open Utilities

let initBehaviourPredictor
    (logInfo: string -> unit)
    (logWarning: string -> unit)
    (logError: string -> unit)
    (fileDir: string)
    (existingRecordings: Guid list)
    (storageLimitAsBytes: int64)
    (memoryLimitAsBytes: int64)
     : Async<unit> =
        async {
            logInfo "Initializing behaviour predictor."
            logInfo <| sprintf $"Got storage limit: {storageLimitAsBytes} {memoryLimitAsBytes}"
            let baseDirectory =
                Assembly.GetExecutingAssembly().CodeBase
                    |> UriBuilder
                    |> _.Path
                    |> Uri.UnescapeDataString
                    |> Path.GetDirectoryName
            init_bert $"{baseDirectory}/main.exe"
            Utilities.logInfo <- logInfo
            Utilities.logWarning <- logWarning
            Utilities.logError <- logError
            Model.userId <- userId
            let initEncoder = async {
                let! _ = encodeText "init"
                logInfo "Encoder init done."
            }

            let fileAsync = async {
                let fileSubDir = "policy/"
                let policyDir = Path.Combine(fileDir, fileSubDir)
                createDirIfDoesNotExist fileDir fileSubDir
                let! fileState, fileData = readStoredPolicy policyDir logWarning
                do! loadModel fileData existingRecordings memoryLimitAsBytes
                PolicyFileHandler.fileHandler <- createFileHandler fileState policyDir storageLimitAsBytes
            }

            let! _ = Async.Parallel [initEncoder; fileAsync]
            ()
        }

let startBehaviourPredictor (userId: EntityId) = 
    logInfo "Starting behaviour predictor."
    Model.userId <- userId
    Async.RunSynchronously <| learnerThread PolicyFileHandler.fileHandler

let clearAllStorage () = false // TODO

// Clear the policy and statistics. Does not affect any active mimics
// This function probably does not have much use outside of testing so it doesn't matter much.
let clearMemory = 
    async {
        startDate <- DateTime.Now
        let! _ = accessLVar learnerLVar <| fun learnerOption ->
            match learnerOption with
            | None -> ()
            | Some learner ->
                Async.RunSynchronously <| async {
                    let! _ = writeLVar learner.gameInputStatisticsLVar <| defaultGameInputStatistics()
                    let! _ = writeLVar modelLVar <| emptyModel()
                    do! putMVar learner.notifyUpdateStatistics 1
                    ()
                }
            ()
        ()
    }

// Called when an audio recording is deleted. Signals to the behaviour predictor that it is no longer in use.
let deleteRecording (fileId: Guid) =
    Async.Start <| async {
        let! _ = accessLVar modelLVar <| fun model ->
            ignore <| model.availableRecordings.Remove(fileId)
        do! deleteRecordingFromMimics fileId
        ()
    }

let userRegisterText
    (gameInput: GameInput)
    = Async.Start <| async {
        logInfo <| sprintf $"{DateTime.UtcNow} Got user register {gameInput}"
        let! _ = accessLVar learnerLVar <| fun learnerOption ->
            match learnerOption with
            | None -> ()
            | Some learner -> postToLearnerHandler learner.gameInputHandler gameInput
        ()
    }

let mimicRegisterText
    (mimicId: Guid)
    (gameInput: GameInput)
    = Async.Start <| async {
        logInfo <| sprintf $"Called mimic register text. {mimicId} {gameInput}"
        let! _ = accessLVar mimicsLVar <| fun mimics ->
            if mimics.ContainsKey mimicId then
                let mimicData = mimics[mimicId]
                logInfo <| sprintf $"Found the mimic. Posting..."
                postToStatisticsUpdater mimicData.statisticsUpdater gameInput
        ()
    }

let userIsActivePing () = Async.Start <| async {
    let! _ = accessLVar learnerLVar <| fun learnerOption ->
        match learnerOption with
        | None -> ()
        | Some learner -> learner.activityHandler.Post(Ping)
    ()
}

let setUserIsInactive () = Async.Start <| async {
    let! _ = accessLVar learnerLVar <| fun learnerOption ->
        match learnerOption with
        | None -> ()
        | Some learner -> learner.activityHandler.Post(SetInactive)
    ()
}