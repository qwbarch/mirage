module Mirage.Unity.Recognition

#nowarn "40"

open System
open System.Collections.Generic
open FSharpPlus
open FSharpx.Control
open Unity.Netcode
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.LVar
open GameNetcodeStuff
open Mirage.Domain.Logger
open Mirage.Domain.Audio.Microphone
open Mirage.Hook.Microphone
open Mirage.Core.Audio.Microphone.Recognition


type RemoteTranscriber() as self =
    inherit NetworkBehaviour()

    let hostAgent = new BlockingQueueAgent<RemoteAction>(Int32.MaxValue)
    let clientAgent = new BlockingQueueAgent<RemoteAction>(Int32.MaxValue)


    let sentenceAgent = new BlockingQueueAgent<RemoteSentenceAction>(Int32.MaxValue)

    /// Players with a <b>RemoteTranscriber</b> instance.
    static member val Players = newLVar <| Map.empty

    member val Player = null with set, get

    member this.Awake() = this.Player <- this.GetComponent<PlayerControllerB>()

    member this.Start() =
        let rec hostConsumer =
            async {
                let! action = hostAgent.AsyncGet()
                let processSentence = processTranscriber MicrophoneSubscriber.Instance.MicrophoneProcessor << TranscribeSentence
                match action with
                    | RemoteStart payload ->
                        logInfo "sentenceAction: RemoteStart"
                        do! processSentence << SentenceStart <|
                            {   playerId = this.Player.playerClientId
                                sentenceId = payload.sentenceId
                            }
                    | RemoteEnd payload ->
                        logInfo "sentenceAction: RemoteEnd"
                        do! processSentence <| SentenceEnd { sentenceId = payload.sentenceId }
                    | RemoteFound payload ->
                        logInfo "sentenceAction: RemoteFound"
                        do! processSentence << SentenceFound <|
                            {   playerId = payload.playerId
                                sentenceId = payload.sentenceId
                                samples = payload.samples
                            }
                do! hostConsumer
            }
        Async.Start(hostConsumer, this.destroyCancellationToken)

        let rec clientConsumer =
            async {
                let! action = clientAgent.AsyncGet()
                match action with
                    | RemoteStart payload ->
                        self.StartSentenceServerRpc <| payload.sentenceId.ToString()
                    | RemoteEnd payload ->
                        self.EndSentenceServerRpc <| payload.sentenceId.ToString()
                    | RemoteFound payload ->
                        if payload.samples.Length > 0 then
                            self.TranscribeSentenceServerRpc(payload.playerId, payload.sentenceId.ToString(), payload.samples)
                do! clientConsumer
            }
        Async.StartImmediate(clientConsumer, this.destroyCancellationToken)

        let rec sentenceConsumer =
            async {
                let! action = sentenceAgent.AsyncGet()
                match action with
                    | RemoteSentenceFound payload ->
                        self.SentenceFoundClientRpc(
                            payload.sentenceId.ToString(),
                            payload.text,
                            payload.avgLogProb,
                            payload.noSpeechProb
                        )
                    | RemoteSentenceEnd payload ->
                        self.SentenceEndClientRpc(
                            payload.sentenceId.ToString(),
                            payload.text,
                            payload.avgLogProb,
                            payload.noSpeechProb
                        )
                do! sentenceConsumer
            }
        Async.StartImmediate(sentenceConsumer, this.destroyCancellationToken)

    override _.OnDestroy() =
        base.OnDestroy()
        dispose hostAgent
        dispose clientAgent

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

    member _.RemoteAction(action) = clientAgent.Add action
    member _.SentenceAction(payload) = sentenceAgent.Add payload

    [<ServerRpc>]
    member this.StartSentenceServerRpc(sentenceId: string) =
        if this.IsHost then
            logInfo $"Sentence start: {sentenceId}"
            hostAgent.Add <|
                RemoteStart
                    {   playerId = this.Player.playerClientId
                        sentenceId = new Guid(sentenceId)
                    }

    [<ServerRpc>]
    member this.EndSentenceServerRpc(sentenceId: string) =
        if this.IsHost then
            logInfo "Sentence end"
            hostAgent.Add <| RemoteEnd { sentenceId = new Guid(sentenceId) }

    [<ServerRpc>]
    member this.TranscribeSentenceServerRpc(playerId, sentenceId: string, samples) =
        if this.IsHost && samples.Length > 0 then
            hostAgent.Add << RemoteFound <|
                {   playerId = playerId
                    sentenceId = new Guid(sentenceId)
                    samples = samples
                }

    [<ClientRpc>]
    member this.SentenceFoundClientRpc(sentenceId: string, text: string, avgLogProb: float32, noSpeechProb: float32) =
        if not this.IsHost then
            logInfo $"SentenceFoundClientrpc. SentenceId: {sentenceId} Text: {text} avgLogProb: {avgLogProb} noSpeechProb: {noSpeechProb}"

    [<ClientRpc>]
    member this.SentenceEndClientRpc(sentenceId: string, text: string, avgLogProb: float32, noSpeechProb: float32) =
        if not this.IsHost then
            logInfo $"SentenceEndClientrpc. SentenceId: {sentenceId} Text: {text} avgLogProb: {avgLogProb} noSpeechProb: {noSpeechProb}"