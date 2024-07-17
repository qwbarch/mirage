module Mirage.Unity.MimicVoice

#nowarn "40"

open System
open FSharpPlus
open FSharpPlus.Data
open FSharpx.Control
open UnityEngine
open Unity.Netcode
open Dissonance.Audio.Playback
open Predictor.MimicPool
open Predictor.Domain
open Mirage.Core.Async.LVar
open Mirage.Hook.Dissonance
open Mirage.Domain.Audio.Recording
open Mirage.Domain.Logger
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer
open Mirage.Unity.Predictor

[<AllowNullLiteral>]
type MimicVoice() as self =
    inherit NetworkBehaviour()

    let recordingManager = RecordingManager()
    let mutable mimicPlayer: MimicPlayer = null
    let mutable predictor: Predictor = null
    let mutable audioStream: AudioStream = null
    let mutable voicePlayback: GameObject = null

    let startVoiceMimic () =
        let mimicVoice =
            map ignore << OptionT.run <| monad {
                try
                    if mimicPlayer.MimickingPlayer = StartOfRound.Instance.localPlayerController then
                        let! recording = OptionT <| getRecording recordingManager
                        do! lift <| audioStream.StreamAudioFromFile recording
                with | error -> logError $"Error occurred while mimicking voice: {error}"
            }
        let rec runMimicLoop =
            async {
                do! mimicVoice
                do! Async.Sleep 5000
                do! runMimicLoop
            }
        Async.StartImmediate(runMimicLoop, self.destroyCancellationToken)

    member this.Awake() =
        mimicPlayer <- this.GetComponent<MimicPlayer>()
        predictor <- this.GetComponent<Predictor>()
        audioStream <- this.GetComponent<AudioStream>()

        voicePlayback <- Object.Instantiate<GameObject> dissonance._playbackPrefab2
        let removeComponent : Type -> unit = Object.Destroy << voicePlayback.GetComponent
        iter removeComponent
            [   typeof<VoicePlayback>
                typeof<SamplePlaybackComponent>
                typeof<PlayerVoiceIngameSettings>
            ]
        voicePlayback.GetComponent<AudioLowPassFilter>().enabled <- true
        audioStream.AudioSource <- voicePlayback.GetComponent<AudioSource>()
        audioStream.AudioSource.loop <- false
        voicePlayback.transform.parent <- audioStream.transform
        voicePlayback.SetActive true
    
    /// Update voice playback object to always be at the same location as the parent.
    member this.LateUpdate() = voicePlayback.transform.position <- this.transform.position

    override this.OnNetworkSpawn() =
        base.OnNetworkSpawn()

        // AudioStream functions require it to be run from the unity thread.
        // mimicInit runs the callback from a different thread, requiring its queued action to have to be passed back to the unity thread.
        let channel = new BlockingQueueAgent<string>(Int32.MaxValue)
        let rec consumer =
            async {
                let! file = channel.AsyncGet()
                logInfo $"Mimic {mimicPlayer.MimicId} requested file: {file}"
                do! audioStream.StreamAudioFromFile file
                do! consumer
            }
        Async.StartImmediate(consumer, this.destroyCancellationToken)

        mimicPlayer.OnSetMimicId.Add <| fun mimicId ->
            if mimicPlayer.MimickingPlayer = StartOfRound.Instance.localPlayerController then
                logInfo $"OnNetworkSpawn mimicId: {mimicPlayer.MimicId}"
                mimicInit mimicId <| fun payload ->
                    logInfo $"Player #{mimicPlayer.MimickingPlayer.playerClientId} sendMimicText is requesting the file: {payload.recordingId}.mp3"
                    channel.Add $"{Application.dataPath}/../Mirage/Recording/{payload.recordingId}.mp3"
                    Async.StartImmediate <| async {
                        let voiceActivityAtom = snd << List.head <| payload.vadTimings
                        let spokeAtom = snd << List.last <| payload.whisperTimings
                        let heardAtom =
                            {   text = spokeAtom.text
                                speakerId = predictor.SpeakerId
                                speakerClass = voiceActivityAtom.speakerId
                                isMimic = true
                                sentenceId = spokeAtom.sentenceId
                                elapsedMillis = spokeAtom.elapsedMillis
                                transcriptionProb = spokeAtom.transcriptionProb
                                nospeechProb = spokeAtom.nospeechProb
                                distanceToSpeaker = 0f
                            }
                        predictor.Register <| SpokeAtom spokeAtom
                        let! playerPredictors = readLVar Predictor.Players
                        flip iter playerPredictors <| fun playerPredictor ->
                            playerPredictor.Register << HeardAtom <|
                                {   heardAtom with
                                        distanceToSpeaker =
                                            Vector3.Distance(
                                                playerPredictor.transform.position,
                                                this.transform.position
                                            )
                                }
                        let! enemyPredictors = readLVar Predictor.Enemies
                        flip iter enemyPredictors <| fun enemyPredictor ->
                            if predictor <> enemyPredictor then
                                enemyPredictor.Register << HeardAtom <|
                                    {   heardAtom with
                                            distanceToSpeaker = 
                                                Vector3.Distance(
                                                    mimicPlayer.MimickingPlayer.transform.position,
                                                    this.transform.position
                                                )
                                    }
                    }
    
    override _.OnNetworkDespawn() =
        base.OnDestroy()
        mimicKill mimicPlayer.MimicId