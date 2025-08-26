module Mirage.Unity.MimicVoice

open IcedTasks
open FSharpPlus
open System
open System.Threading.Tasks
open UnityEngine
open Unity.Netcode
open Dissonance.Audio.Playback
open Mirage.Hook.Dissonance
open Mirage.Core.Task.Loop
open Mirage.Domain.Logger
open Mirage.Domain.Config
open Mirage.Domain.Setting
open Mirage.Domain.Audio.Recording
open Mirage.Domain.Null
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer


type MimicVoice() =
    inherit NetworkBehaviour()

    let recordingManager = RecordingManager()
    let random = Random()

    let mutable voicePlayback = null
    let mutable audioStream = null
    let mutable mimicPlayer = null
    let mutable enemyAI = null
    
    member this.Awake() =
        audioStream <- this.GetComponent<AudioStream>()
        mimicPlayer <- this.GetComponent<MimicPlayer>()
        enemyAI <- this.GetComponent<EnemyAI>()

        voicePlayback <- Object.Instantiate<GameObject> <| getDissonance()._playbackPrefab2
        voicePlayback.name <- "Mirage"
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
    
    member this.Start() =
        let startVoiceMimicking () =
            forever <| fun () -> valueTask {
                try
                    if isNotNull enemyAI
                        && not enemyAI.isEnemyDead
                        && isNotNull mimicPlayer.MimickingPlayer
                        && Object.ReferenceEquals(mimicPlayer.MimickingPlayer, StartOfRound.Instance.localPlayerController)
                    then
                        let! recording = getRecording recordingManager
                        if recording.IsSome then
                            do! audioStream.StreamOpusFromFile recording.Value
                with
                    | :? TaskCanceledException as error -> raise error
                    | error -> logError $"Error occurred while mimicking voice: {error}"
                let delay =
                    if enemyAI :? MaskedPlayerEnemy then
                        random.Next(getConfig().minimumDelayMasked, getConfig().maximumDelayMasked + 1)
                    else
                        random.Next(getConfig().minimumDelayNonMasked, getConfig().maximumDelayNonMasked + 1)

                do! Task.Delay(delay, this.destroyCancellationToken)
            }
        try ignore <| startVoiceMimicking()
        with :? TaskCanceledException as _ -> ()

    /// Update voice playback object to always be at the same location as the parent.
    member this.LateUpdate() = voicePlayback.transform.position <- this.transform.position

    member _.FixedUpdate() =
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
                if Object.ReferenceEquals(mimicPlayer.MimickingPlayer, localPlayer) then
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
                    || not (getConfig().mimicVoiceWhileHiding) && maskedEnemyIsHiding()
                    || isMimicLocalPlayerMuted()
                    || isNotHauntedOrDisappearedDressGirl()
                    || spectatingPlayer.isInsideFactory = enemyAI.isOutside
                    || getSettings().muteVoiceMimic