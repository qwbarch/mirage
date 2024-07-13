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
let REPLY_BIAS = 10.0

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
    (heardSims: List<float>)
    (spokeSims: List<float>)
    (policy: KeyValuePair<DateTime, CompressedObservation * FutureAction> seq)
    (observation: Observation)
    (rngSource: RandomSource) : List<float * FutureAction> =
    let flattened = 
        let temp = List<float * float * DateTime * CompressedObservation * FutureAction>()
        let mutable i = 0
        // printfn "----------------------------------------------"
        // printfn "obs %A" observation
        for kv in policy do
            let policyObs, action = fst kv.Value, snd kv.Value
            temp.Add((heardSims[i], spokeSims[i], kv.Key, policyObs, action))
            i <- i + 1
        temp

    let mutable maxNoAction = -10.0
    let mutable timeNoActionCount = 0
    let result = flip map flattened <| fun (heardSim, spokeSim, _, policyObs, action) ->
        let talkBias =
            match action with
            | NoAction -> 0.0
            | QueueAction _ -> config.SCORE_TALK_BIAS

        let speakTimeCost = SCORE_SPEAK_FACTOR * speakOrHearDiffToCost policyObs.lastSpoke observation.lastSpoke rngSource false
        let hearTimeCost = speakOrHearDiffToCost policyObs.lastHeard observation.lastHeard rngSource false
        let speakOrHearTimeCost = max speakTimeCost hearTimeCost
        let totalCost = heardSim + spokeSim + talkBias + speakOrHearTimeCost
        match action with
        | NoAction -> 
            if totalCost > maxNoAction then
                logInfo <| sprintf "NoAction %f %f %f %f %f %O %O" totalCost spokeSim heardSim speakTimeCost hearTimeCost policyObs action
                maxNoAction <- totalCost
                timeNoActionCount <- 1
            if abs (totalCost - maxNoAction) < 1e-5 then
                timeNoActionCount <- timeNoActionCount + 1
            ()
        | QueueAction _ -> 
            logInfo <| sprintf "action %f %f %f %f %f %O %O" totalCost spokeSim heardSim speakTimeCost hearTimeCost policyObs action
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


let sampleAction (oppositeOrdPolicy: Policy) (observation: Observation) (rngSource: RandomSource) : FutureAction = 
    if oppositeOrdPolicy.Count = 0 then
        NoAction
    else
        let policy = Seq.rev oppositeOrdPolicy

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
        let scores = computeScores heardSims spokeSims policy observation rngSource
        let sampled = sample scores rngSource
        logInfo <| sprintf "Sampled %A" sampled
        snd sampled
let observationToFutureAction (internalPolicy: LVar<Policy>) (observation : Observation) (rngSource: RandomSource) : Async<Option<FutureAction>> =
    async {
        let! res = accessLVar internalPolicy <| fun policy ->
            if policy.Count = 0 then
                None
            else
                Some <| sampleAction policy observation rngSource
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
        let! futureActionOption = observationToFutureAction internalPolicy observation rngSource
        if futureActionOption.IsSome then
            sendToActionEmitter futureActionOption.Value
        ()
    }