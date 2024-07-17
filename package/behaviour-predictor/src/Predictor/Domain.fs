module Predictor.Domain

open System
open System.Collections.Generic
open FSharpx.Control
open Predictor.DisposableAsync
open Mirage.Core.Async.LVar
open Mirage.Core.Async.MVar

type TextEmbedding = float32 array

type EitherSpokeHeard = Spoke | Heard

type EntityId = 
    | Guid of Guid
    | Int of uint64

type EntityClass = EntityId

// Must be const
// Separate spoke and heard inputs to properly handle a user talking with its own mimic.
type SpokeAtom =
    {   text: string
        sentenceId: Guid
        elapsedMillis: int
        transcriptionProb: double
        nospeechProb: double
        // TODO
        // languageId: int32
    }

type AudioInfo = 
    {   fileId: Guid
        duration: int
    }

type VoiceActivityAtom =
    {   speakerId: EntityId
        prob: double
        distanceToSpeaker: float32 // In the case that an entity A spoke and the VoiceActivityAtom is sent back to A, distanceToSpeaker would be 0.
    }

type SpokeRecordingAtom =
    {   spokeAtom: SpokeAtom
        whisperTimings: (int * SpokeAtom) list
        vadTimings: (int * VoiceActivityAtom) list
        audioInfo: AudioInfo
    }

type HeardAtom =
    {   text: string
        speakerClass: EntityClass
        speakerId: EntityId
        isMimic: bool
        sentenceId: Guid
        elapsedMillis: int
        transcriptionProb: double
        nospeechProb: double
        distanceToSpeaker: float32
        // TODO
        // languageId: int32
    }

type GameInput =
    | SpokeAtom of SpokeAtom
    | SpokeRecordingAtom of SpokeRecordingAtom
    | VoiceActivityAtom of VoiceActivityAtom
    | HeardAtom of HeardAtom
    // | ConsoleAtom of ConsoleAtom

type ActivityAtom =
    | Ping
    | SetInactive

// A type for a middle step between raw GameInputs and an Observation
type GameInputStatistics =
    {   
        mutable lastSpoke: (DateTime * SpokeAtom) option
        lastHeard: SortedDictionary<EntityId, DateTime * HeardAtom>
        voiceActivityQueue: SortedDictionary<EntityId, DateTime>
    }

type StatisticsUpdater = MailboxProcessor<DateTime * GameInput>
type ObservationGenerator = DisposableAsync

type PartialObservation =
    {   spokeEmbedding: Option<string * TextEmbedding>
        heardEmbedding: Option<string * TextEmbedding>
        lastSpokeDate: DateTime
        lastHeardDate: DateTime
    }
type Observation =
    {   time: DateTime
        spokeEmbedding: Option<string * TextEmbedding>
        heardEmbedding: Option<string * TextEmbedding>
        lastSpoke: int
        lastHeard: int
        // recentSpeakers: List<EntityClass> // TODO
    }

type ObsEmbedding =
    | Value of Option<string * TextEmbedding>
    | Prev

type CompressedObservation =
    {   time: DateTime
        spokeEmbedding: ObsEmbedding
        heardEmbedding: ObsEmbedding
        lastSpoke: int
        lastHeard: int
    }
    override this.ToString() =
        let strings = List<string>()
        strings.Add(this.time.ToString() + " " + this.time.Millisecond.ToString())
        match this.spokeEmbedding with
        | Prev -> strings.Add "Previous"
        | Value None -> strings.Add "None"
        | Value (Some (text, _)) -> strings.Add("Spoke: " + text)
        match this.heardEmbedding with
        | Prev -> strings.Add "Previous"
        | Value None -> strings.Add "None"
        | Value (Some (text, _)) -> strings.Add("Heard: " + text)
        String.concat ", " strings

type CompressedObservationFileFormat =
    {   time: DateTime
        spokeEmbedding: ObsEmbedding
        heardEmbedding: ObsEmbedding
        lastSpoke: int
        lastHeard: int
    }

type AudioResponse =
    {   fileId: Guid
        embedding: Option<string * TextEmbedding>
        whisperTimings: (int * SpokeAtom) list
        vadTimings: (int * VoiceActivityAtom) list
        duration: int
    }
    override this.ToString() =
        match this.embedding with
        | None -> "None"
        | Some (text, _) -> sprintf "\"%s\"" text


type QueueActionInfo = 
    { action: AudioResponse
      delay: int }
    override this.ToString() = this.action.ToString()

type FutureAction =
    | NoAction
    | QueueAction of QueueActionInfo
    override this.ToString() =
        match this with
        | NoAction -> "NoAction"
        | QueueAction q -> "QueueAction " + q.ToString()

type Policy = SortedDictionary<DateTime, CompressedObservation * FutureAction>

type PolicyUpdateMessage = 
    | ObsActionPair of DateTime * CompressedObservation * FutureAction
    | RemoveRecording of Guid

type PolicyDeleteMessage = RemovePolicy of (DateTime * CompressedObservation * FutureAction) list

type MimicMessage =
    {   recordingId: Guid
        whisperTimings: (int * SpokeAtom) list
        vadTimings: (int * VoiceActivityAtom) list
    }

type MimicPolicyUpdater = AutoCancelAgent<PolicyUpdateMessage>
type FutureActionGenerator = DisposableAsync
type MimicData =
    {   mimicClass: Guid // Equal to the id of the person that this mimic is mimicking
        killSignal: MVar<int>
        sendMimicText: MimicMessage -> unit
        internalPolicy: LVar<Policy>

        policyUpdater: MimicPolicyUpdater

        currentStatistics: LVar<GameInputStatistics>
        notifyUpdateStatistics: MVar<int>
        statisticsUpdater: StatisticsUpdater

        observationChannel: LVar<DateTime -> Observation>
        observationGenerator: ObservationGenerator

        futureActionGenerator: FutureActionGenerator
    }

type LearnerMessageHandler = AutoCancelAgent<DateTime * GameInput>
type ActivityHandler = AutoCancelAgent<ActivityAtom>
type LearnerAccess =
    {   gameInputHandler: LearnerMessageHandler
        activityHandler: ActivityHandler
        gameInputStatisticsLVar: LVar<GameInputStatistics>
        notifyUpdateStatistics: MVar<int>
    }

type Model =
    {   policy: Policy
        availableRecordings: HashSet<Guid>
        // Store some helper data to do some operations faster
        mutable lastSpokeEncoding: Option<string * TextEmbedding> option
        mutable lastHeardEncoding: Option<string * TextEmbedding> option
        mutable copies: int
        mutable bytes: int64
        mutable bytesLimit: int64
    }

type FilePath = string

type PolicyFileFormat =
    {   creationDate: DateTime // We maintain this for an easy way of ordering files
        data: (CompressedObservationFileFormat * FutureAction) array
    }

type PolicyFileInfo =
    {   creationDate: DateTime // Sort by DateTime first!!
        name: FilePath
    }
    
type PolicyFileState =
    {   dateToFileInfo: Dictionary<DateTime, PolicyFileInfo> // Any observation datetime to the file that stores that observation
        files: SortedSet<PolicyFileInfo>
    }

type PolicyFileMessage =
    | Add of Observation * FutureAction
    | Update of DateTime * FutureAction

type PolicyFileHandler = MailboxProcessor<PolicyFileMessage>

type RandomSource = MathNet.Numerics.Random.Mcg31m1