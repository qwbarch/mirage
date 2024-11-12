module Mirage.Unity.MirageVoice

#nowarn "40"

open FSharpPlus
open FSharpPlus.Data
open System
open UnityEngine
open Unity.Netcode
open Dissonance.Audio.Playback
open Mirage.Hook.Dissonance
open Mirage.Domain.Audio.Recording
open Mirage.Domain.Logger
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer
open Mirage.Domain.Config
open Mirage.Domain.Setting
open System.Diagnostics

let private random = Random()

type MimicVoice() as self =
    inherit NetworkBehaviour()

    let mutable voicePlayback: GameObject = null
    let mutable audioStream: AudioStream = null
    let mutable mimicPlayer: MimicPlayer = null
    let mutable enemyAI: EnemyAI = null

    let startVoiceMimic () =
        //let logMimicVoice guid message =
        //    if not (isNull enemyAI) && not (isNull mimicPlayer.MimickingPlayer) && mimicPlayer.MimickingPlayer = StartOfRound.Instance.localPlayerController then
        //        logInfo $"MimicVoice ({guid}): {message}"
        let mimicVoice guid =
            map ignore << OptionT.run <| monad {
                try
                    //let log = logMimicVoice guid
                    //log "mimicVoice start."
                    if not (isNull enemyAI)
                       && not (isNull mimicPlayer.MimickingPlayer)
                       && mimicPlayer.MimickingPlayer = StartOfRound.Instance.localPlayerController
                    then
                        //log "mimicVoice getRecording."
                        let! recording = OptionT getRecording
                        //log "mimicVoice StreamAudioFromFile."
                        let sw = Stopwatch()
                        sw.Start()
                        do! lift <| audioStream.StreamAudioFromFile(recording, guid)
                        sw.Stop()
                        //let elapsedSecs = (float sw.ElapsedMilliseconds / 1000.0).ToString("F2")
                        //log $"After mimicVoice (mimic loop). Elapsed time: {elapsedSecs}"
                        //log "mimicVoice finished."
                with | error -> logError $"Error occurred while mimicking voice: {error}"
            }
        let rec runMimicLoop =
            async {
                let guid = Guid.NewGuid()
                //let log = logMimicVoice guid
                let delay =
                    if enemyAI :? MaskedPlayerEnemy then
                        random.Next(getConfig().minimumDelayMasked, getConfig().maximumDelayMasked + 1)
                    else
                        random.Next(getConfig().minimumDelayNonMasked, getConfig().maximumDelayNonMasked + 1)
                //log "Before mimicVoice (mimic loop)"
                do! mimicVoice guid
                //let delaySecs = (float delay / 1000.0).ToString("F2")
                //log $"Sleeping for {delaySecs} seconds"
                do! Async.Sleep delay
                //log "Finished sleeping."
                do! runMimicLoop
            }
        Async.StartImmediate(runMimicLoop, self.destroyCancellationToken)
    
    member val IsMimickingVoice = false with get, set

    member this.Awake() =
        audioStream <- this.GetComponent<AudioStream>()
        mimicPlayer <- this.GetComponent<MimicPlayer>()
        enemyAI <- this.GetComponent<EnemyAI>()

        voicePlayback <- Object.Instantiate<GameObject> <| getDissonance()._playbackPrefab2
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
    
    member _.Start() = startVoiceMimic()

    /// Update voice playback object to always be at the same location as the parent.
    member this.LateUpdate() = voicePlayback.transform.position <- this.transform.position

    member _.Update() =
        if isNull mimicPlayer.MimickingPlayer || isNull enemyAI then
            audioStream.AudioSource.mute <- true
        else
            let localPlayer = StartOfRound.Instance.localPlayerController
            let spectatingPlayer = if isNull localPlayer.spectatedPlayerScript then localPlayer else localPlayer.spectatedPlayerScript
            let isMimicLocalPlayerMuted () =
                let alwaysMute = getSettings().localPlayerVolume = 0f
                let muteWhileNotDead =
                    not (getConfig().enableMimicVoiceWhileAlive)
                        && not mimicPlayer.MimickingPlayer.isPlayerDead
                mimicPlayer.MimickingPlayer = localPlayer && (muteWhileNotDead || alwaysMute)
            let isNotHauntedOrDisappearedDressGirl () =
                enemyAI :? DressGirlAI && (
                    let dressGirlAI = enemyAI :?> DressGirlAI
                    let isVisible = dressGirlAI.staringInHaunt || dressGirlAI.moveTowardsDestination && dressGirlAI.movingTowardsTargetPlayer
                    not dressGirlAI.hauntingLocalPlayer || not isVisible
                )
            let maskedEnemyIsHiding () =
                enemyAI :? MaskedPlayerEnemy
                    && Vector3.Distance(enemyAI.transform.position, (enemyAI :?> MaskedPlayerEnemy).shipHidingSpot) < 0.4f
                    && enemyAI.agent.speed = 0f
                    && (enemyAI :?> MaskedPlayerEnemy).crouching
            audioStream.AudioSource.volume <-
                if mimicPlayer.MimickingPlayer = localPlayer then
                    getSettings().localPlayerVolume
                else
                    // Might as well use 1f as the default if any of those fields are null.
                    if isNull mimicPlayer.MimickingPlayer || isNull mimicPlayer.MimickingPlayer.currentVoiceChatAudioSource then
                        1f
                    else
                        mimicPlayer.MimickingPlayer.currentVoiceChatAudioSource.volume
            audioStream.AudioSource.outputAudioMixerGroup <-
                SoundManager.Instance.playerVoiceMixers[int mimicPlayer.MimickingPlayer.playerClientId]
            audioStream.AudioSource.mute <-
                enemyAI.isEnemyDead
                    || (not (getConfig().mimicVoiceWhileHiding) && maskedEnemyIsHiding())
                    || isMimicLocalPlayerMuted()
                    || isNotHauntedOrDisappearedDressGirl()
                    || spectatingPlayer.isInsideFactory = enemyAI.isOutside