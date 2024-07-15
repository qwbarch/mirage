module Predictor.Learner

open System
open DisposableAsync
open Predictor.MimicPool
open ObservationGenerator
open Predictor.Domain
open FSharpx.Control
open Model
open Config
open Utilities
open System.Linq
open System.Collections.Generic
open Embedding
open Mirage.Core.Async.LVar
open Mirage.Core.Async.MVar
open DomainBytes

let learnerLVar : LVar<LearnerAccess option> = newLVar(None)

let addEmptyObservation
    (fileHandler: PolicyFileHandler)
    (observation: Observation) =
    async {
        let! _ = accessLVar modelLVar <| fun model ->
            if not <| model.policy.ContainsKey(observation.time) then
                let compressedObservation : CompressedObservation = 
                    {   time = observation.time
                        spokeEmbedding = toObsEmbedding model.lastSpokeEncoding observation.spokeEmbedding
                        heardEmbedding = toObsEmbedding model.lastHeardEncoding observation.heardEmbedding
                        lastSpoke = observation.lastSpoke
                        lastHeard = observation.lastHeard
                    }

                // Update the model
                model.policy[observation.time] <- (compressedObservation, NoAction)
                match compressedObservation.spokeEmbedding with
                | Prev -> ()
                | Value (newValue) -> model.lastSpokeEncoding <- Some newValue

                match compressedObservation.heardEmbedding with
                | Prev -> ()
                | Value (newValue) -> model.lastHeardEncoding <- Some newValue

                Async.RunSynchronously <| sendUpdateToMimics compressedObservation.time compressedObservation NoAction
                fileHandler.Post <| Add (observation, NoAction)

                model.bytes <- model.bytes + 8L + getSizeCompressedObs compressedObservation + getSizeAction NoAction
                Model.notifyMemoryChange()
        ()
    }

let addSpokeResponse 
    (arrivalTime: DateTime)
    (spokeRecordingAtom: SpokeRecordingAtom) 
    (fileHandler: PolicyFileHandler)
    = 
    async {
        // Get the embedding pertaining to only the user response in isolation.
        // Note that this is different from the spoken embedding from the recent statistics
        let spokeAtom = spokeRecordingAtom.spokeAtom
        let spokeAtomStart = arrivalTime.AddMilliseconds(-spokeAtom.elapsedMillis)
        let! spokeAtomEmbedding = encodeText spokeAtom.text

        let! _ = accessLVar modelLVar <| fun model ->
            let predicate (kv: KeyValuePair<DateTime, CompressedObservation * FutureAction>) = kv.Key <= spokeAtomStart
            try
                let relevantKV = Enumerable.First(model.policy, predicate)
                let (relObs, relFuture) = relevantKV.Value
                let timeDifferenceMillis = timeSpanToMillis <| spokeAtomStart - relObs.time
                if timeDifferenceMillis < 3 * config.MIL_PER_OBS then
                    let queueAction: QueueActionInfo = {
                        action = 
                            {   fileId=spokeRecordingAtom.audioInfo.fileId 
                                embedding = spokeAtomEmbedding
                                whisperTimings = spokeRecordingAtom.whisperTimings
                                vadTimings = spokeRecordingAtom.vadTimings
                                duration=spokeRecordingAtom.audioInfo.duration 
                            }
                        delay = timeDifferenceMillis
                    }
                    let prevObs, prevAction = model.policy[relObs.time]

                    model.policy[relObs.time] <- (relObs, QueueAction queueAction)
                    ignore <| model.availableRecordings.Add(queueAction.action.fileId)
                    Async.RunSynchronously <| sendUpdateToMimics relObs.time relObs (QueueAction queueAction)
                    fileHandler.Post <| Update (relObs.time, QueueAction queueAction)
                    model.bytes <- model.bytes - getSizeAction prevAction - getSizeCompressedObs prevObs + getSizeAction (QueueAction queueAction) + getSizeCompressedObs relObs
                    Model.notifyMemoryChange()
                    logInfo $"Added a response."
                    ()
                else
                    logInfo $"Large time gap {timeDifferenceMillis} {relObs}"
            with
            | :? InvalidOperationException -> 
                logInfo $"No observation found {model.policy.Count}" // No observation found
            | _ -> logWarning "Could not find a relevant observation"
            ()
        ()
    }

// Look for either game inputs or responses. Send the data to the right location depending on the response.
let createLearnerMessageHandler 
    (fileHandler: PolicyFileHandler)
    (statisticsUpdater: StatisticsUpdater)
    = 
    AutoCancelAgent.Start(fun inbox ->
    let rec loop () =
        async {
            let! arrivalTime, gameInput = inbox.Receive()
            // If the person spoke and it corresponds to a saved recording, we update the model.
            logInfo $"{gameInput}"
            match gameInput with
            | SpokeAtom _ ->
                ()
            | SpokeRecordingAtom spokeRecordingAtom ->
                do! addSpokeResponse arrivalTime spokeRecordingAtom fileHandler
            | HeardAtom _ ->
                ()
            | VoiceActivityAtom vaAtom ->
                ()

            // Either way, send it down the line to the statistics updater
            postToStatisticsUpdater statisticsUpdater gameInput
            do! loop()
        }
    loop()
)

let postToLearnerHandler
    (handler: LearnerMessageHandler)
    (gameInput: GameInput) =
    handler.Post(DateTime.UtcNow, gameInput)

let learnerObservationSampler
    (fileHandler: PolicyFileHandler)
    (observationChannel: LVar<DateTime -> Observation>)
    (isActiveLVar: LVar<bool>) =
    repeatAsync config.MIL_PER_OBS <| async {
        let! isActive = readLVar isActiveLVar
        if isActive then
            let timeStart = DateTime.UtcNow
            let! observationProducer = readLVar observationChannel
            do! addEmptyObservation fileHandler <| observationProducer timeStart
    }

let createActivityHandler (isActiveLVar: LVar<bool>) : ActivityHandler = AutoCancelAgent<ActivityAtom>.Start(fun inbox ->
    let rec loop () =
        async {
            let! messageOption = inbox.TryReceive(config.AFK_MILLIS)
            match messageOption with
            | None ->
                let! prevIsActive = writeLVar isActiveLVar false
                if prevIsActive then
                    logInfo "afk detected"
                ()
            | Some Ping ->
                let! _ = writeLVar isActiveLVar true
                ()
            | Some SetInactive ->
                let! prevIsActive = writeLVar isActiveLVar false
                if prevIsActive then
                    logInfo "Set inactive."
                ()
            do! loop()
        }
    loop()
)

let learnerThread 
    (fileHandler: PolicyFileHandler) =
    async {
        let isActiveLVar = newLVar(false)
        let activityHandler = createActivityHandler isActiveLVar
        let currentStatistics = newLVar(defaultGameInputStatistics());
        let notifyUpdateStatistics = createEmptyMVar<int>()
        let statisticsUpdater = createStatisticsUpdater currentStatistics notifyUpdateStatistics 
        let statisticsCutoffHandler = createStatisticsCutoffHandler userId currentStatistics notifyUpdateStatistics

        let messageHandler = createLearnerMessageHandler fileHandler statisticsUpdater

        let observationChannel = newLVar(insertObsTime defaultPartialObservation)
        let _ : ObservationGenerator = 
            startAsyncAsDisposable <| createObservationGeneratorAsync userId currentStatistics notifyUpdateStatistics observationChannel 

        let _ = startAsyncAsDisposable <| learnerObservationSampler fileHandler observationChannel isActiveLVar

        let learner : LearnerAccess =
            {   gameInputHandler = messageHandler
                activityHandler = activityHandler
                gameInputStatisticsLVar = currentStatistics
                notifyUpdateStatistics = notifyUpdateStatistics
            }
        let! _ = writeLVar learnerLVar <| Some learner
        ()
    }
