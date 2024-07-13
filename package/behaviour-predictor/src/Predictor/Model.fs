module Predictor.Model

open Domain
open System
open System.Collections.Generic
open Utilities
open Mirage.Core.Async.LVar
open PolicyFileHandler

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
}

let modelLVar = newLVar(emptyModel())

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

let loadModel (fileDatas: ((CompressedObservationFileFormat * FutureAction) array) seq) (existingRecordingsList: Guid list) = async {
    logInfo "Called loadModel"
    let! _ = accessLVar modelLVar <| fun model ->
        for existingRecording in existingRecordingsList do
            ignore <| model.availableRecordings.Add(existingRecording)

        logInfo <| sprintf $"Got {model.availableRecordings.Count} recordings."
        for data in fileDatas do
            let mutable failCount = 0
            for (obsFileFormat, action) in data do
                let obs = fromCompressedObsFileFormat obsFileFormat
                let time = obs.time
                let succ = model.policy.TryAdd(time, (obs, action))
                if not succ then
                    failCount <- 1
                ()

            if failCount > 0 then
                logWarning $"Found duplicate date entries: {failCount}"
                // TODO initialize lastSpokeEncoding, lastHeardEncoding, though I don't think this really matters outside of a small performance bump on startup
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