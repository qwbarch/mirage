module Mirage.Unity.MimicVoice

#nowarn "40"

open System
open Dissonance
open Dissonance.Audio.Playback
open FSharpPlus
open UnityEngine
open System.Collections.Generic
open Mirage.Core.Field
open Mirage.Core.Logger
open Mirage.Core.Monad
open Mirage.Core.Audio.Recording
open Mirage.Core.Config
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer

let private get<'A> = getter<'A> "MimicVoice"

/// <summary>
/// A component that attaches to an <b>EnemyAI</b> to mimic a player's voice.
/// </summary>
[<AllowNullLiteral>]
type MimicVoice() as self =
    inherit MonoBehaviour()

    let random = new System.Random()

    let MimicPlayer = field<MimicPlayer>()
    let AudioStream = field<AudioStream>()
    let EnemyAI = field()
    let Playback = field()
    let getMimicPlayer = get MimicPlayer "MimicPlayer"
    let getAudioStream = get AudioStream "AudioStream"
    let getEnemyAI = get EnemyAI "EnemyAI"

    let recordings = new List<string>()

    let getRecording () =
        match getConfig().imitateMode with
            | ImitateRandom -> getRandomRecording random
            | ImitateNoRepeat ->
                if recordings.Count = 0 then
                    recordings.AddRange <| getRecordings()
                // Recordings can still be empty.
                if recordings.Count = 0 then 
                    None
                else
                    let index = random.Next recordings.Count
                    let recording = recordings[index]
                    recordings.RemoveAt index
                    Some recording

    let startVoiceMimic (enemyAI: EnemyAI) =
        let mimicVoice () =
            handleResult <| monad' {
                let methodName = "mimicVoice"
                let! mimicPlayer = getMimicPlayer methodName
                let! audioStream = getAudioStream methodName
                ignore <| monad' {
                    let! player = mimicPlayer.GetMimickingPlayer()
                    let! recording = getRecording()
                    try
                        if player = StartOfRound.Instance.localPlayerController then
                            if player.IsHost then
                                audioStream.StreamAudioFromFile recording
                            else
                                audioStream.UploadAndStreamAudioFromFile(
                                    player.actualClientId,
                                    recording
                                )
                    with | error ->
                        logError $"Failed to mimic voice: {error}"
                }
            }
        let rec runMimicLoop =
            let config = getConfig()
            let delay =
                if enemyAI :? MaskedPlayerEnemy then
                    random.Next(config.imitateMinDelay, config.imitateMaxDelay + 1)
                else
                    random.Next(config.imitateMinDelayNonMasked, config.imitateMaxDelayNonMasked + 1)
            async {
                mimicVoice()
                return! Async.Sleep delay
                return! runMimicLoop
            }
        runAsync self.destroyCancellationToken runMimicLoop

    member this.Awake() =
        setNullable MimicPlayer <| this.gameObject.GetComponent<MimicPlayer>()
        setNullable EnemyAI <| this.gameObject.GetComponent<EnemyAI>()
        let audioStream = this.gameObject.GetComponent<AudioStream>()
        setNullable AudioStream audioStream

        let dissonance = Object.FindObjectOfType<DissonanceComms>()
        let playback = Object.Instantiate<GameObject> <| dissonance._playbackPrefab2
        let removeComponent : Type -> unit = Object.Destroy << playback.GetComponent
        iter removeComponent
            [   typeof<VoicePlayback>
                typeof<SamplePlaybackComponent>
                typeof<PlayerVoiceIngameSettings>
            ]
        playback.transform.parent <- audioStream.transform
        set Playback playback

        let audioSource = playback.GetComponent<AudioSource>()
        audioStream.SetAudioSource audioSource
        audioSource.loop <- false
        audioSource.gameObject.SetActive true

    member _.Start() =
        handleResult <| monad' {
            let! enemyAI = getEnemyAI "Start"
            startVoiceMimic enemyAI
        }

    member this.LateUpdate() =
        // Update the playback component to always be on the same position as the parent.
        // This ensures audio plays from the correct position.
        flip iter Playback.Value <| fun playback ->
            playback.transform.position <- this.transform.position

    member _.Update() =
        let mute () =
            handleResult <| monad {
                let! audioStream = getAudioStream "mute"
                audioStream.GetAudioSource().mute <- true
            }
        handleResultWith mute <| monad' {
            let methodName = "Update"
            let! audioStream = getAudioStream methodName
            let audioSource = audioStream.GetAudioSource()
            let! mimicPlayer = getMimicPlayer methodName
            let! enemyAI = getEnemyAI methodName
            let localPlayer = StartOfRound.Instance.localPlayerController
            let spectatingPlayer = if isNull localPlayer.spectatedPlayerScript then localPlayer else localPlayer.spectatedPlayerScript
            match mimicPlayer.GetMimickingPlayer() with
                | None -> audioSource.mute <- true
                | Some mimickingPlayer ->
                    let isMimicLocalPlayerMuted () =
                        let alwaysMute = getLocalConfig().AlwaysMuteLocalPlayer.Value
                        let muteWhileNotDead =
                            getConfig().muteLocalPlayerVoice
                                && not mimickingPlayer.isPlayerDead
                        mimickingPlayer = localPlayer && (muteWhileNotDead || alwaysMute)
                    let isNotHauntedOrDisappearedDressGirl () =
                        enemyAI :? DressGirlAI && (
                            let dressGirlAI = enemyAI :?> DressGirlAI
                            let isVisible = dressGirlAI.staringInHaunt || dressGirlAI.moveTowardsDestination && dressGirlAI.movingTowardsTargetPlayer
                            not <| dressGirlAI.hauntingLocalPlayer || not isVisible
                        )
                    let maskedEnemyIsHiding () =
                        enemyAI :? MaskedPlayerEnemy && Vector3.Distance(enemyAI.transform.position, (enemyAI :?> MaskedPlayerEnemy).shipHidingSpot) < 0.4f
                    audioSource.mute <-
                        enemyAI.isEnemyDead
                            || maskedEnemyIsHiding()
                            || isMimicLocalPlayerMuted()
                            || isNotHauntedOrDisappearedDressGirl()
                            || spectatingPlayer.isInsideFactory = enemyAI.isOutside
        }