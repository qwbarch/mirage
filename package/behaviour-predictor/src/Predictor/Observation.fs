module Predictor.ObservationGenerator

open FSharp.Core
open Predictor.Domain
open System.Collections.Generic
open System
open Embedding
open Config
open Utilities
open DisposableAsync
open System.Linq
open Mirage.Core.Async.LVar
open Mirage.Core.Async.MVar

let defaultGameInputStatistics () =
    {   lastSpoke = None
        lastHeard = SortedDictionary()
        voiceActivityQueue = SortedDictionary()
    }

let mutable startDate = DateTime.Now
let defaultPartialObservation : PartialObservation =
    {   spokeEmbedding = None
        heardEmbedding = None
        lastSpokeDate = startDate
        lastHeardDate = startDate
    }

let insertObsTime (obs: PartialObservation) (newTime: DateTime) : Observation = 
    {   time = newTime 
        spokeEmbedding = obs.spokeEmbedding
        heardEmbedding = obs.heardEmbedding
        lastHeard = timeSpanToMillis <| newTime - obs.lastHeardDate
        lastSpoke = timeSpanToMillis <| newTime - obs.lastSpokeDate
    }

let reduceSize (queue: SortedSet<DateTime * 'T>) =
    let cutoff = DateTime.UtcNow.AddMilliseconds(-config.VOICE_BUFFER)
    let mutable reduced = false
    while queue.Count > 1 && fst queue.Min < cutoff do
        reduced <- true
        let _ = queue.Remove(queue.Min)
        ()
    reduced

let createStatisticsUpdater 
    (currentStatistics: LVar<GameInputStatistics>) 
    (notifyUpdate: MVar<int>)
    : StatisticsUpdater = 
    MailboxProcessor<DateTime * GameInput>.Start(fun inbox ->
        let rec loop () =
            async {
                do! async {
                    // Consume all unprocessed inputs
                    let! arrivalTime, item = inbox.Receive()
                    logInfo <| sprintf $"Got {item}"
                    let! __ = accessLVar currentStatistics <| fun stats ->
                        match item with
                        | SpokeAtom spokeAtom ->
                            stats.lastSpoke <- Some (arrivalTime, spokeAtom)
                        | SpokeRecordingAtom spokeRecordingAtom ->
                            let spokeAtom = spokeRecordingAtom.spokeAtom
                            stats.lastSpoke <- Some (arrivalTime, spokeAtom)
                        | HeardAtom heardAtom ->
                            if heardAtom.distanceToSpeaker <= float32 45 then
                                if not <| stats.lastHeard.ContainsKey heardAtom.speakerId then
                                    stats.lastHeard.Add(heardAtom.speakerId, (arrivalTime, heardAtom))
                                else
                                    replaceDict stats.lastHeard heardAtom.speakerId (arrivalTime, heardAtom)
                            else
                                logInfo "Too far. Rejected."
                        | VoiceActivityAtom vaAtom -> 
                            if not <| stats.voiceActivityQueue.ContainsKey vaAtom.speakerId then
                                stats.voiceActivityQueue.Add(vaAtom.speakerId, arrivalTime)
                            replaceDict stats.voiceActivityQueue vaAtom.speakerId arrivalTime

                    logInfo "Notifying..."
                    ignore <| tryPutMVar notifyUpdate 1 
                }
                
                do! loop()
            }
        loop()
    )

let postToStatisticsUpdater
    (statisticsUpdater: StatisticsUpdater)
    (message: GameInput)
    = statisticsUpdater.Post((DateTime.UtcNow,  message))

let statisticsToPartialObservation (entityId: EntityId) (statistics: GameInputStatistics) : Async<PartialObservation> =
    async {
        let spokeString, spokeDate =
            match statistics.lastSpoke with
            | None -> "", startDate
            | Some (spokeDate, spokeAtom) -> spokeAtom.text, spokeDate

        let heardString, heardDate =
            if statistics.lastHeard.Count = 0 then
                "", startDate
            else
                let mutable latestTime = startDate
                let mutable latestHeard = snd <| statistics.lastHeard.Values.First()
                for time, heardAtom in statistics.lastHeard.Values do
                    if time > latestTime then
                        latestTime <- time
                        latestHeard <- heardAtom
                latestHeard.text, latestTime

        // TODO properly do batching so that it is impossible for these two to be in separate BERT runs
        let! embedding = Async.Parallel [encodeText spokeString; encodeText heardString]
        logInfo <| sprintf $"Spoke: {spokeString}, {spokeDate} as id: {entityId}"
        logInfo <| sprintf $"Heard: {heardString}, {heardDate}"

        let spokeEmbedding = embedding[0]
        let heardEmbedding = embedding[1]
        let partialObservation : PartialObservation =
            {   spokeEmbedding = spokeEmbedding
                heardEmbedding = heardEmbedding
                lastSpokeDate = spokeDate
                lastHeardDate = heardDate
                    // logInfo <| sprintf $"Voice activity queue: {statistics.voiceActivityQueue.Count} {entityId}"
                    // for k in statistics.voiceActivityQueue.Keys do
                    //     let v = statistics.voiceActivityQueue[k]
                    //     let tt = timeSpanToMillis <| DateTime.UtcNow - v
                    //     logInfo <| sprintf $"kv pair {k} {tt}"
                        
                    // let heard = Map.remove entityId <| sortedDictToMap statistics.voiceActivityQueue
                    // if heard.Count = 0 then
                    //     startDate
                    // else
                    //     logInfo <| sprintf $"Selected timing {heard |> Map.toSeq |> Seq.map snd |> Seq.max}"
                    //     heard |> Map.toSeq |> Seq.map snd |> Seq.max
            }   
        return partialObservation
    }

let createObservationGeneratorAsync 
    (entityId: EntityId)
    (currentStatisticsLVar: LVar<GameInputStatistics>)
    (notifyUpdateStatistics: MVar<int>)
    (observationChannel: LVar<DateTime -> Observation>) : Async<unit> =
        let rec loop () =
            async {
                let! __ = takeMVar notifyUpdateStatistics
                logInfo <| sprintf $"Got an update notification {entityId}"
                let! statisticsSnapshot = accessLVar currentStatisticsLVar <| fun stats ->
                    {   lastSpoke = stats.lastSpoke
                        lastHeard = SortedDictionary(stats.lastHeard)
                        voiceActivityQueue = SortedDictionary(stats.voiceActivityQueue)
                    }
                
                let! partialObservation = statisticsToPartialObservation entityId statisticsSnapshot
                let! __ = writeLVar observationChannel (insertObsTime partialObservation)
                do! loop()
            }
        
        loop()

let reduceSpoke 
    (stats: GameInputStatistics)
    (lastActivity: DateTime) =
    let mutable reduced = false
    let cutoff = DateTime.UtcNow.AddMilliseconds(-config.VOICE_BUFFER)
    if stats.lastSpoke.IsSome then
        let arrivalTime, lastSpoke = stats.lastSpoke.Value
        if arrivalTime < cutoff then
            reduced <- true
            stats.lastSpoke <- None
    reduced

let reduceHeard
    (queue: SortedDictionary<EntityId, DateTime * HeardAtom>) : bool
    =
    let mutable reduced = false
    let cutoff = DateTime.UtcNow.AddMilliseconds(-config.VOICE_BUFFER)
    let toRemove: List<EntityId> = List()
    for kv in queue do
        let arrivalTime, heardAtom = kv.Value
        if arrivalTime < cutoff then
            reduced <- true
            toRemove.Add(kv.Key)

    for key in toRemove do
        ignore <| queue.Remove(key)
    reduced

let createStatisticsCutoffHandler 
    (entityId: EntityId)
    (currentStatisticsLVar: LVar<GameInputStatistics>) 
    (notifyUpdate: MVar<int>) = startAsyncAsDisposable << repeatAsync config.MIL_PER_OBS <| async {
    let! _ = accessLVar currentStatisticsLVar <| fun stats ->
        let reduced = 
            let userLastDateTime =
                if stats.voiceActivityQueue.ContainsKey(entityId) then
                    stats.voiceActivityQueue[entityId]
                else
                    DateTime.MinValue
            let spokeReduce = reduceSpoke stats userLastDateTime
            let heardReduce = reduceHeard stats.lastHeard
            spokeReduce || heardReduce
        if reduced then
            ignore <| tryPutMVar notifyUpdate 1
        ()
    ()
}