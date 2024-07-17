module Predictor.ActionSelector

open Domain
open System
open Utilities
open Config
open System.Collections.Generic
open Embedding
open FSharpPlus
open Mirage.Core.Async.LVar

// When comparing to a past observation with no speech context,
// Add a score if in the current observation there is also no speech
let NOSPEECH_NOSPEECH = 0.0
// Add a score if the current observation has speech and the prior observation had no speech
let SPEECH_NOSPEECH = 0.0

// Notes:
// We should keep TIME_SIGNAL high to encourage mimic responses
// Given that the mimic responds, we should keep SIM_SCALE somewhat lower to encourage a nice balance of responses.
let SIM_OFFSET = -0.2
let SIM_SCALE = 5.0

let TIME_AREA = 2.0
let TIME_SIGNAL = 9.0
let SCORE_SPEAK_FACTOR = 1.0

// Bias the mimic towards replying to other players
let REPLY_BIAS = 1.0

let simTransform (x: float) = SIM_SCALE * (x + SIM_OFFSET)

let speakOrHearDiffToCost (policyDiff: int) (curDiff: int) (rngSource: RandomSource) (verbose: bool) =
    if policyDiff > 10000000 || curDiff > 10000000 then
        0.0
    else
        let l : int = min policyDiff <| max 300 (policyDiff / 2)
        let r = max (l + (int <| float config.MIL_PER_OBS * TIME_AREA)) <| policyDiff + policyDiff / 2
        let split = max 1 <| (r - l) / config.MIL_PER_OBS
        let zzz = clamp 0.0 1.0 <| TIME_AREA / float split
        if verbose then
            logInfo <| sprintf $"diffInfo {policyDiff} {curDiff} {l} {r} {split} {zzz}"

        if l <= curDiff && curDiff <= r then
            let prob = clamp 0.0 1.0 <| TIME_AREA / float split
            if verbose then
                logInfo <| sprintf $"diffInfo2 {policyDiff} {curDiff} {l} {r} {split} {prob}"
            if rngSource.NextDouble() < prob then
                TIME_SIGNAL
            else
                0.0
        else
            0.0

let computeScores 
    (heardSomethingList: List<bool>)
    (heardSims: List<float>)
    (spokeSims: List<float>)
    (policy: KeyValuePair<DateTime, CompressedObservation * FutureAction> seq)
    (observation: Observation)
    (rngSource: RandomSource) : List<float * FutureAction> =
    let flattened = 
        let temp = List<bool * float * float * DateTime * CompressedObservation * FutureAction>()
        let mutable i = 0
        // printfn "----------------------------------------------"
        // printfn "obs %A" observation
        for kv in policy do
            let policyObs, action = fst kv.Value, snd kv.Value
            temp.Add((heardSomethingList[i], heardSims[i], spokeSims[i], kv.Key, policyObs, action))
            i <- i + 1
        temp

    let heardInObs = observation.heardEmbedding.IsSome
    let mutable maxNoAction = -10.0
    let mutable timeNoActionCount = 0
    let result = flip map flattened <| fun (heardSomething, heardSim, spokeSim, _, policyObs, action) ->
        let talkBias =
            match action with
            | NoAction -> 0.0
            | QueueAction _ -> config.SCORE_TALK_BIAS

        let speakTimeCost = SCORE_SPEAK_FACTOR * speakOrHearDiffToCost policyObs.lastSpoke observation.lastSpoke rngSource false
        let hearTimeCost = speakOrHearDiffToCost policyObs.lastHeard observation.lastHeard rngSource false
        let speakOrHearTimeCost = max speakTimeCost hearTimeCost
        let replyBonus = 
            match action with
            | NoAction -> 0.0
            // | QueueAction _ -> if heardSomething then REPLY_BIAS else 0.0
            | QueueAction _ -> if heardInObs then REPLY_BIAS else 0.0
        let totalCost = replyBonus + heardSim + spokeSim + talkBias + speakOrHearTimeCost

        match action with
        | NoAction -> 
            if totalCost > maxNoAction then
                logInfo <| sprintf "NoAction %f %f %f %f %f %f %O %O" totalCost replyBonus spokeSim heardSim speakTimeCost hearTimeCost policyObs action
                maxNoAction <- totalCost
                timeNoActionCount <- 1
            if abs (totalCost - maxNoAction) < 1e-5 then
                timeNoActionCount <- timeNoActionCount + 1
            ()
        | QueueAction _ -> 
            logInfo <| sprintf "action %f %f %f %f %f %f %O %O" totalCost replyBonus spokeSim heardSim speakTimeCost hearTimeCost policyObs action
            // logInfo <| sprintf $"Diff {policyObs.lastHeard} {observation.lastHeard}"
            // if heardSim > 3.0 then
            //     logInfo <| sprintf "action %f %f %f %f %f %O %O" totalCost spokeSim heardSim speakTimeCost hearTimeCost policyObs action
                // logInfo <| 
                //     let _ = speakOrHearDiffToCost policyObs.lastHeard observation.lastHeard rngSource true
                    // ""
        // | QueueAction _ -> logInfo <| sprintf "action %f %f %d %O %O" totalCost hearTimeCost policyObs.lastHeard policyObs action

        totalCost, action
    logInfo <| sprintf $"maxNoAction {maxNoAction} {timeNoActionCount}"
    logInfo <| sprintf $"Observation {observation.lastHeard} {observation.lastSpoke}"
    result

let sample (unnormScores: List<float * FutureAction>) (rngSource: RandomSource) =
    // TODO use a heap instead of sorting
    let scoresMean = (map (fun (s, _) -> s) unnormScores |> sum) / float unnormScores.Count
    let scoresOrd : List<float * int> = map (fun i -> (fst unnormScores[i] - scoresMean, i)) <| List(Array.init unnormScores.Count (fun i -> i))

    scoresOrd.Sort()
    scoresOrd.Reverse()

    let scores : float array =
        let scoresList: List<float> = map fst scoresOrd
        scoresList.ToArray()

    let distribution = softmax scores
    let scoresOrdChoiceInd = weightedSample distribution
    let choice = snd scoresOrd[scoresOrdChoiceInd]
    unnormScores[choice]


let sampleActionRandom (oppositeOrdPolicy: Policy) (observation: Observation) (rngSource: RandomSource) : FutureAction = 
    if oppositeOrdPolicy.Count = 0 then
        NoAction
    else
        let policy = Seq.rev oppositeOrdPolicy

        let getHasValue 
            (policyObsEmbs: seq<ObsEmbedding>) =
            let accum: List<bool> = List()
            let mutable last: bool = false
            for obsEmb in policyObsEmbs do
                last <-
                    match obsEmb with
                    | Prev -> last
                    | Value None -> false
                    | Value (Some _) -> true
                accum.Add(last)
            accum

        let computeSims
            (policyObsEmbs: seq<ObsEmbedding>)
            (target: Option<string * TextEmbedding>) =
            let sims : List<float> = List()
            let mutable lastSim = SPEECH_NOSPEECH
            for obsEmb in policyObsEmbs do
                lastSim <-
                    match obsEmb with
                    | Prev -> lastSim
                    | Value None -> 
                        match target with
                        | None -> NOSPEECH_NOSPEECH
                        | Some (_, _) -> SPEECH_NOSPEECH
                    | Value (Some (_, textEmbedding)) -> 
                        match target with
                        | None -> SPEECH_NOSPEECH
                        | Some (_, observationEmbedding) -> simTransform <| embeddingSim textEmbedding observationEmbedding
                sims.Add(lastSim)
            sims

        let policyHeardEmbs, policySpokeEmbs =
            let heard = List<ObsEmbedding>()
            let spoke = List<ObsEmbedding>()
            for kv in policy do
                let comp = fst kv.Value
                heard.Add(comp.heardEmbedding)
                spoke.Add(comp.spokeEmbedding)
            heard, spoke

        let heardSims = computeSims policyHeardEmbs observation.heardEmbedding
        let spokeSims = computeSims policySpokeEmbs observation.spokeEmbedding
        let spokeSomethingList = getHasValue policySpokeEmbs
        let heardSomethingList = getHasValue policyHeardEmbs
        let spokeTot = Seq.sumBy (fun b -> if b then 1 else 0) spokeSomethingList
        let heardTot = Seq.sumBy (fun b -> if b then 1 else 0) heardSomethingList
        logInfo <| sprintf $"Heard count: {heardTot} Spoke count: {spokeTot}"
        let scores = computeScores heardSomethingList heardSims spokeSims policy observation rngSource
        let sampled = sample scores rngSource
        logInfo <| sprintf "Sampled %A" sampled
        snd sampled

let getClosestAction (policy: Policy) (internalRecordings: HashSet<Guid>) (action: FutureAction): FutureAction option =
    match action with
    | NoAction -> 
        invalidOp "Wrong argument."
    | QueueAction queueAction ->
        match queueAction.action.embedding with
        | None -> None
        | Some (_, embedding) ->
            let accum: List<float * FutureAction> = List();
            for kv in policy do
                let _, possibleAction = kv.Value
                match possibleAction with
                | NoAction -> ()
                | QueueAction possibleQueueAction ->
                    match possibleQueueAction.action.embedding with
                    | None -> ()
                    | Some (_, possibleEmbedding) ->
                        if internalRecordings.Contains(possibleQueueAction.action.fileId) then
                            accum.Add((embeddingSim embedding possibleEmbedding, possibleAction))
            if accum.Count = 0 then
                None
            else
                let res = Seq.maxBy fst accum
                logInfo <| sprintf $"Found the best match with similarity: {fst res}. {snd res}"
                Some <| snd res


// TODO this way of deleting does not respect the compression trick. Fix?
let reducePolicy (policy: Policy) =
    if policy.Count < 10000 then
        // Enforce a baseline count
        ()
    else
        let prevSize = policy.Count
        let removeCount = min policy.Count <| int(float(policy.Count) * 0.3)
        let toRemove = take removeCount policy
        for kv in toRemove do
            ignore <| policy.Remove(kv.Key)
        
        logInfo <| sprintf $"Culling the policy. Previous size: {prevSize}, After size: {policy.Count}"
        ()

let sampleAction (oppositeOrdPolicy: Policy) (internalRecordings: HashSet<Guid>) (observation: Observation) (rngSource: RandomSource) : FutureAction = 
    let timeStart = DateTime.UtcNow
    let sampled = 
        let sampled = sampleActionRandom oppositeOrdPolicy observation rngSource
        match sampled with
        | NoAction -> sampled
        | QueueAction queueAction ->
            if internalRecordings.Contains(queueAction.action.fileId) then
                sampled
            else
                logInfo <| sprintf $"Recording does not exist: {queueAction.action.fileId}. Finding the closest match..."
                let closest = getClosestAction oppositeOrdPolicy internalRecordings sampled
                match closest with
                | None -> NoAction
                | Some action -> action
    let timeEnd = DateTime.UtcNow
    logInfo <| sprintf $"Took time to get sample: {timeSpanToMillis <| timeEnd - timeStart}"

    // Delete some of the policy if it took too long to get the action
    if timeSpanToMillis (timeEnd - timeStart) > 200 then
        reducePolicy oppositeOrdPolicy
    sampled

let observationToFutureAction (internalPolicy: LVar<Policy>) (internalRecordingsLVar: LVar<HashSet<Guid>>) (observation : Observation) (rngSource: RandomSource) : Async<Option<FutureAction>> =
    async {
        let! res = accessLVar internalPolicy <| fun policy ->
            Async.RunSynchronously <| async {
                let! innerRes = accessLVar internalRecordingsLVar <| fun internalRecordings ->
                    if policy.Count = 0 then
                        None
                    else
                        Some <| sampleAction policy internalRecordings observation rngSource
                return innerRes
            }
        return res
    }

let createFutureActionGeneratorAsync
    (internalPolicy: LVar<Policy>)
    (internalRecordings: LVar<HashSet<Guid>>)
    (observationChannel: LVar<DateTime -> Observation>)
    (sendToActionEmitter: FutureAction -> unit)
    (rngSource: RandomSource) =
    repeatAsync config.MIL_PER_OBS <| async {
        let timeStart = DateTime.UtcNow
        let! observationProducer = readLVar observationChannel
        let observation = observationProducer timeStart
        let! futureActionOption = observationToFutureAction internalPolicy internalRecordings observation rngSource
        if futureActionOption.IsSome then
            sendToActionEmitter futureActionOption.Value
        ()
    }