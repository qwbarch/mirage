module Mirage.Unity.Recognition

#nowarn "40"

open System
open FSharpPlus
open FSharpx.Control
open Unity.Netcode
open Mirage.Core.Async.LVar
open GameNetcodeStuff
open Mirage.Domain.Logger
open Mirage.Domain.Audio.Microphone
open Mirage.Hook.Microphone
open Mirage.Core.Audio.Microphone.Recognition
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Microphone.Detection

[<AllowNullLiteral>]
type RemoteTranscriber() as self =
    inherit NetworkBehaviour()

    let mutable player = null

    let fromVADFrame vadFrame = (vadFrame.elapsedTime, vadFrame.probability)
    let toVADFrame (elapsedTime, probability) =
        {   elapsedTime = elapsedTime
            probability = probability
        }

    let requestAgent = new BlockingQueueAgent<RequestAction>(Int32.MaxValue)
    let responseAgent = new BlockingQueueAgent<ResponseAction>(Int32.MaxValue)

    /// Players with a <b>RemoteTranscriber</b> instance.
    static member val Players = newLVar zero

    member this.Awake() = player <- this.GetComponent<PlayerControllerB>()

    member this.Start() =
        let rec consumer =
            async {
                let! action = requestAgent.AsyncGet()
                logInfo "RemoteTranscriber consumer AsyncGet"
                if this.IsHost then
                    let processRemote = processTranscriber MicrophoneSubscriber.Instance.MicrophoneProcessor << TranscribeBatched
                    match action with
                        | RequestStart payload ->
                            logInfo "batchedAction: BatchedStart"
                            do! processRemote << BatchedStart <|
                                {   fileId = payload.fileId
                                    playerId = player.playerClientId
                                    sentenceId = payload.sentenceId
                                }
                        | RequestEnd payload ->
                            logInfo "batchedAction: BatchedEnd"
                            do! processRemote << BatchedEnd <|
                                {   sentenceId = payload.sentenceId
                                    vadTimings = payload.vadTimings
                                }
                        | RequestFound payload ->
                            logInfo "batchedAction: BatchedFound"
                            do! processRemote << BatchedFound <|
                                {   playerId = payload.playerId
                                    sentenceId = payload.sentenceId
                                    samples = payload.samples
                                    vadFrame = payload.vadFrame
                                }
                else
                    match action with
                        | RequestStart payload ->
                            logInfo "RemoteTranscribe consumer (non-host): RequestStart"
                            self.StartSentenceServerRpc(payload.fileId.ToString(), payload.sentenceId.ToString())
                        | RequestEnd payload ->
                            logInfo "RemoteTranscribe consumer (non-host): RequestEnd"
                            let (elapsedTimes, probabilities) = unzip << map fromVADFrame <| Array.ofSeq payload.vadTimings
                            self.EndSentenceServerRpc(
                                payload.sentenceId.ToString(),
                                elapsedTimes,
                                probabilities
                            )
                        | RequestFound payload ->
                            logInfo "RemoteTranscribe consumer (non-host): RequestFound"
                            if payload.samples.Length > 0 then
                                self.TranscribeSentenceServerRpc(
                                    payload.playerId,
                                    payload.sentenceId.ToString(),
                                    payload.samples,
                                    payload.vadFrame.elapsedTime,
                                    payload.vadFrame.probability
                                )
                logInfo "RemoteTranscriber consumer finished"
                do! consumer
            }
        if this.IsHost then
            Async.Start(consumer, this.destroyCancellationToken)
        else
            Async.StartImmediate(consumer, this.destroyCancellationToken)

        let rec producer =
            async {
                let! action = responseAgent.AsyncGet()
                logInfo "RemoteTranscriber producer AsyncGet"
                match action with
                    | ResponseFound payload ->
                        self.SentenceFoundClientRpc(
                            payload.fileId.ToString(),
                            payload.sentenceId.ToString(),
                            payload.transcription.text,
                            payload.transcription.avgLogProb,
                            payload.transcription.noSpeechProb,
                            payload.transcription.startTime,
                            payload.transcription.endTime,
                            payload.vadFrame.elapsedTime,
                            payload.vadFrame.probability
                        )
                    | ResponseEnd payload->
                        let (elapsedTimes, probabilities) = unzip << map fromVADFrame <| Array.ofSeq payload.vadTimings
                        self.SentenceEndClientRpc(
                            payload.fileId.ToString(),
                            payload.sentenceId.ToString(),
                            payload.transcription.text,
                            payload.transcription.avgLogProb,
                            payload.transcription.noSpeechProb,
                            payload.transcription.startTime,
                            payload.transcription.endTime,
                            elapsedTimes,
                            probabilities
                        )
                logInfo "RemoteTranscriber producer finished"
                do! producer
            }
        Async.StartImmediate(producer, this.destroyCancellationToken)

    override _.OnDestroy() =
        base.OnDestroy()
        dispose requestAgent
        dispose responseAgent

    override this.OnNetworkSpawn() =
        base.OnNetworkSpawn()
        Async.StartImmediate
            << modifyLVar RemoteTranscriber.Players
            <| Map.add player.playerClientId this
    
    override _.OnNetworkDespawn() =
        base.OnNetworkDespawn()
        Async.StartImmediate
            << modifyLVar RemoteTranscriber.Players
            <| Map.remove player.playerClientId

    member _.SendRequest(action) = requestAgent.Add action
    member _.SendResponse(action) = responseAgent.Add action

    [<ServerRpc>]
    member this.StartSentenceServerRpc(fileId: string, sentenceId: string) =
        if this.IsHost then
            logInfo $"Sentence start. FileId: {fileId}"
            logInfo $"Sentence start. SentenceId: {sentenceId}"
            requestAgent.Add <|
                RequestStart
                    {   fileId = Guid fileId
                        playerId = player.playerClientId
                        sentenceId = Guid sentenceId
                    }

    [<ServerRpc>]
    member this.EndSentenceServerRpc(sentenceId: string, elapsedTimes, probabilities) =
        if this.IsHost then
            logInfo "Sentence end"
            requestAgent.Add << RequestEnd <|
                {   sentenceId = new Guid(sentenceId)
                    vadTimings = List.ofArray << map toVADFrame <| zip elapsedTimes probabilities
                }

    [<ServerRpc>]
    member this.TranscribeSentenceServerRpc(playerId, sentenceId: string, samples: Samples, elapsedTime, probability) =
        if this.IsHost && samples.Length > 0 then
            requestAgent.Add << RequestFound <|
                {   vadFrame =
                        {   elapsedTime = elapsedTime
                            probability = probability
                        }
                    playerId = playerId
                    sentenceId = new Guid(sentenceId)
                    samples = samples
                }

    [<ClientRpc>]
    member this.SentenceFoundClientRpc(fileId: string, sentenceId: string, text, avgLogProb, noSpeechProb, startTime, endTime, elapsedTime, probability) =
        if not this.IsHost then
            logInfo $"SentenceFoundClientrpc. SentenceId: {sentenceId} Text: {text} avgLogProb: {avgLogProb} noSpeechProb: {noSpeechProb}"
            onTranscribe (newLVar []) (Guid sentenceId) << TranscribeFound <| // TODO: FIX THIS
                {   fileId = Guid fileId
                    vadFrame =
                        {   elapsedTime = elapsedTime
                            probability = probability
                        }
                    transcription =
                        {   text = text
                            avgLogProb = avgLogProb
                            noSpeechProb = noSpeechProb
                            startTime = startTime
                            endTime = endTime
                        }
                }

    [<ClientRpc>]
    member this.SentenceEndClientRpc(fileId: string, sentenceId: string, text, avgLogProb, noSpeechProb, startTime, endTime, elapsedTimes, probabilities) =
        if not this.IsHost then
            logInfo $"SentenceEndClientrpc. SentenceId: {sentenceId} Text: {text} avgLogProb: {avgLogProb} noSpeechProb: {noSpeechProb}"
            let vadTimings = toVADFrame <!> zip elapsedTimes probabilities
            onTranscribe (newLVar []) (Guid sentenceId) << TranscribeEnd <| // TODO: FIX THIS
                {   fileId = Guid fileId
                    vadFrame = Array.last vadTimings
                    vadTimings = List.ofSeq vadTimings
                    transcription =
                        {   text = text
                            avgLogProb = avgLogProb
                            noSpeechProb = noSpeechProb
                            startTime = startTime
                            endTime = endTime
                        }
                }