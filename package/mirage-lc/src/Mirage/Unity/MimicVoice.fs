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

    let recordingManager = RecordingManager()
    let mutable voicePlayback: GameObject = null
    let mutable audioStream: AudioStream = null
    let mutable mimicPlayer: MimicPlayer = null
    let mutable enemyAI: EnemyAI = null

    let startVoiceMimic () =
        let mimicVoice debug =
            map ignore << OptionT.run <| monad {
                try
                    if not (isNull enemyAI)
                       && not (isNull mimicPlayer.MimickingPlayer)
                       && mimicPlayer.MimickingPlayer = StartOfRound.Instance.localPlayerController
                    then
                        debug "Before getRecording."
                        let! recording = OptionT <| getRecording recordingManager
                        debug $"Found recording: {recording}"
                        do! lift <| audioStream.StreamAudioFromFile(recording, debug)
                with | error -> logError $"Error occurred while mimicking voice: {error}"
            }
        let rec runMimicLoop =
            async {
                let guid = Guid.NewGuid()
                let sw = Stopwatch.StartNew()
                let debug message =
                    if false then
                    //if not (isNull enemyAI) && not (isNull mimicPlayer) && not (isNull mimicPlayer.MimickingPlayer) then
                        let s = sw.Elapsed.TotalMilliseconds.ToString("F2")
                        logInfo $"{enemyAI.enemyType.enemyName} - {guid} - Elapsed: {s} - {message}"
                let delay =
                    if enemyAI :? MaskedPlayerEnemy then
                        random.Next(getConfig().minimumDelayMasked, getConfig().maximumDelayMasked + 1)
                    else
                        random.Next(getConfig().minimumDelayNonMasked, getConfig().maximumDelayNonMasked + 1)
                debug "Before mimicVoice"
                do! mimicVoice debug
                debug $"After mimicVoice. Sleeping for {float delay / 1000.0} seconds."
                let sw2 = Stopwatch.StartNew()
                do! Async.Sleep delay
                let s = sw2.Elapsed.TotalMilliseconds.ToString("F2")
                debug $"Finished sleeping. Waited for {s} seconds."
                do! runMimicLoop
            }
        Async.StartImmediate(runMimicLoop, self.destroyCancellationToken)
    
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