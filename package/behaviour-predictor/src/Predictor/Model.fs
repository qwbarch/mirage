module Predictor.Model

open Domain
open System
open System.Collections.Generic
open Utilities
open Mirage.Core.Async.LVar
open PolicyFileHandler
open FSharpPlus
open DomainBytes
open FSharpx.Control

let mutable userId = Int 0uL

// Sort by decreasing time since iteration forwards quickly is possible but idk how to iterate backwards.
type ModelPolicyComparator() =
    interface IComparer<DateTime> with
        member this.Compare(x, y) =
            compare y x

let emptyModel () : Model = {
    policy = SortedDictionary(ModelPolicyComparator())
    availableRecordings = HashSet()
    lastSpokeEncoding = None
    lastHeardEncoding = None
    copies = 1
    bytes = 0
    bytesLimit = 0
}

let memorySubscribersLVar: LVar<List<AutoCancelAgent<PolicyDeleteMessage>>> = newLVar(List())
let modelLVar = newLVar(emptyModel())

let notifyMemoryChange() = Async.Start <| async {
        logInfo "Notified memory change."
        let! toRemove = accessLVar modelLVar <| fun model ->
            let accum: List<DateTime * CompressedObservation * FutureAction> = List()
            logInfo <| sprintf $"Current bytes: {model.bytes}, Copies: {model.copies}"
            while model.policy.Count > 0 && model.bytes * int64(model.copies) > model.bytesLimit do
                let kv = Seq.head model.policy
                let dateTime = kv.Key
                let obs, action = kv.Value
                let remBytes = 8L + getSizeCompressedObs obs + getSizeAction action
                model.bytes <- model.bytes - remBytes

                accum.Add((dateTime, obs, action))
                ignore <| model.policy.Remove(dateTime)
                ()
            accum
        
        if toRemove.Count > 0 then
            logInfo <| sprintf $"Deleting {toRemove.Count} items"
            let! _ = accessLVar memorySubscribersLVar <| fun memorySubscribers ->
                for subscriber in memorySubscribers do
                    subscriber.Post(RemovePolicy <| Seq.toList toRemove)
            ()
    }

let copyModelData : Async<Policy * HashSet<Guid>> =
    async {
        let! policyContentsCopy, existingRecordingsCopy = accessLVar modelLVar <| fun model ->
            let policyContents = List<KeyValuePair<DateTime, CompressedObservation * FutureAction>>()
            for kv in model.policy do
                policyContents.Add(kv)
            
            let existingRecordingsContents: List<Guid> = List()
            for existingRecording in model.availableRecordings do
                existingRecordingsContents.Add(existingRecording)
            policyContents, existingRecordingsContents
        
        let policy = SortedDictionary<DateTime, CompressedObservation * FutureAction>(ModelPolicyComparator())
        for (kv: KeyValuePair<DateTime, CompressedObservation * FutureAction>) in policyContentsCopy do
            policy.Add(kv.Key, kv.Value)

        let existingRecordings: HashSet<Guid> = HashSet()
        for existingRecording in existingRecordingsCopy do
            ignore <| existingRecordings.Add(existingRecording)
        logInfo <| sprintf $"Spawning mimic. Policy size: {policy.Count}, Recordings: {existingRecordings.Count}"
        return policy, existingRecordings
    }

let loadModel (fileDatas: ((CompressedObservationFileFormat * FutureAction) array) seq) (existingRecordingsList: Guid list) (bytesLimit: int64) = async {
    logInfo "Called loadModel"
    let! _ = accessLVar modelLVar <| fun model ->
        model.bytesLimit <- bytesLimit

        for existingRecording in existingRecordingsList do
            ignore <| model.availableRecordings.Add(existingRecording)

        for data in fileDatas do
            let mutable failCount = 0
            for (obsFileFormat, action) in data do
                let obs = fromCompressedObsFileFormat obsFileFormat
                let time = obs.time
                let succ = model.policy.TryAdd(time, (obs, action))
                model.bytes <- model.bytes + 8L + getSizeCompressedObs obs + getSizeAction action
                if not succ then
                    failCount <- 1
                ()

            if failCount > 0 then
                logWarning $"Found duplicate date entries: {failCount}"
                // TODO initialize lastSpokeEncoding, lastHeardEncoding, though I don't think this really matters outside of a small performance bump on startup

        logInfo <| sprintf $"Got {model.availableRecordings.Count} recordings. Total bytes: {model.bytes}"
    ()
}

let printModel = 
    async {
        let! _ = accessLVar modelLVar <| fun model ->
            for kv in model.policy do
                let obs = kv.Key
                let act = kv.Value
                printfn "%s" <| "[" + obs.ToString() + "] -> " + act.ToString()
        ()
    }