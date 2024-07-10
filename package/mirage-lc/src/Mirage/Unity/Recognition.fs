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

[<AllowNullLiteral>]
type RemoteTranscriber() as self =
    inherit NetworkBehaviour()

    let requestAgent = new BlockingQueueAgent<RequestAction>(Int32.MaxValue)
    let responseAgent = new BlockingQueueAgent<ResponseAction>(Int32.MaxValue)

    /// Players with a <b>RemoteTranscriber</b> instance.
    static member val Players = newLVar <| Map.empty

    member val Player = null with set, get

    member this.Awake() = this.Player <- this.GetComponent<PlayerControllerB>()

    member this.Start() =
        let rec consumer =
            async {
                let! action = requestAgent.AsyncGet()
                if this.IsHost then
                    let processRemote = processTranscriber MicrophoneSubscriber.Instance.MicrophoneProcessor << TranscribeBatched
                    match action with
                        | RequestStart payload ->
                            logInfo "batchedAction: BatchedStart"
                            do! processRemote << BatchedStart <|
                                {   fileId = payload.fileId
                                    playerId = this.Player.playerClientId
                                    sentenceId = payload.sentenceId
                                }
                        | RequestEnd payload ->
                            logInfo "batchedAction: BatchedEnd"
                            do! processRemote <| BatchedEnd { sentenceId = payload.sentenceId }
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
                            self.StartSentenceServerRpc(payload.playerId.ToString(), payload.sentenceId.ToString())
                        | RequestEnd payload ->
                            self.EndSentenceServerRpc <| payload.sentenceId.ToString()
                        | RequestFound payload ->
                            if payload.samples.Length > 0 then
                                self.TranscribeSentenceServerRpc(
                                    payload.playerId,
                                    payload.sentenceId.ToString(),
                                    payload.samples,
                                    payload.vadFrame.elapsedTime,
                                    payload.vadFrame.probability
                                )
                do! consumer
            }
        Async.StartImmediate(consumer, this.destroyCancellationToken)

        let rec producer =
            async {
                let! action = responseAgent.AsyncGet()
                let respond rpc (payload: ResponsePayload) = rpc(
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
                match action with
                    | ResponseFound payload -> respond self.SentenceFoundClientRpc payload
                    | ResponseEnd payload-> respond self.SentenceEndClientRpc payload
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
            <| Map.add this.Player.playerClientId this
    
    override this.OnNetworkDespawn() =
        base.OnNetworkDespawn()
        Async.StartImmediate
            << modifyLVar RemoteTranscriber.Players
            <| Map.remove this.Player.playerClientId

    member _.SendRequest(action) = requestAgent.Add action
    member _.SendResponse(action) = responseAgent.Add action

    [<ServerRpc>]
    member this.StartSentenceServerRpc(fileId: string, sentenceId: string) =
        if this.IsHost then
            logInfo $"Sentence start: {sentenceId}"
            requestAgent.Add <|
                RequestStart
                    {   fileId = Guid fileId
                        playerId = this.Player.playerClientId
                        sentenceId = Guid sentenceId
                    }

    [<ServerRpc>]
    member this.EndSentenceServerRpc(sentenceId: string) =
        if this.IsHost then
            logInfo "Sentence end"
            requestAgent.Add <| RequestEnd { sentenceId = new Guid(sentenceId) }

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
            onTranscribe (Guid sentenceId) << TranscribeFound <|
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
    member this.SentenceEndClientRpc(fileId: string, sentenceId: string, text, avgLogProb, noSpeechProb, startTime, endTime, elapsedTime, probability) =
        if not this.IsHost then
            logInfo $"SentenceEndClientrpc. SentenceId: {sentenceId} Text: {text} avgLogProb: {avgLogProb} noSpeechProb: {noSpeechProb}"
            onTranscribe (Guid sentenceId) << TranscribeEnd <|
                {   fileId = Guid fileId
                    vadFrame =
                        {   elapsedTime = elapsedTime
                            probability = probability
                        }
                    vadTimings = []
                    transcription =
                        {   text = text
                            avgLogProb = avgLogProb
                            noSpeechProb = noSpeechProb
                            startTime = startTime
                            endTime = endTime
                        }
                }