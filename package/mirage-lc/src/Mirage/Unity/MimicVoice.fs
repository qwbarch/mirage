module Mirage.Unity.MimicVoice

#nowarn "40"

open System
open FSharpPlus
open FSharpPlus.Data
open UnityEngine
open Unity.Netcode
open Dissonance.Audio.Playback
open Mirage.Hook.Dissonance
open Mirage.Domain.Audio.Recording
open Mirage.Domain.Logger
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer
open Predictor.MimicPool
open Unity.Collections
open FSharpx.Control

type MimicVoice() as self =
    inherit NetworkBehaviour()

    let mimicId = new NetworkVariable<FixedString64Bytes>()

    let recordingManager = RecordingManager()
    let mutable mimicPlayer: MimicPlayer = null
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

    member this.Start() =
        if this.IsHost then
            mimicId.Value <- FixedString64Bytes(Guid.NewGuid().ToString())
    
    /// Update voice playback object to always be at the same location as the parent.
    member this.LateUpdate() = voicePlayback.transform.position <- this.transform.position

    override this.OnNetworkSpawn () =
        base.OnNetworkSpawn()
        // AudioStream functions require it to be run from the unity thread.
        // mimicInit runs the callback from a different thread, requiring its queued action to have to be passed back to the unity thread.
        let channel = new BlockingQueueAgent<string>(Int32.MaxValue)
        let rec consumer =
            async {
                let! file = channel.AsyncGet()
                logInfo $"Mimic {mimicId.Value.Value} requested file: {file}"
                do! audioStream.StreamAudioFromFile file
                do! consumer
            }
        Async.StartImmediate(consumer, this.destroyCancellationToken)
        let onMimicIdChanged _ (guid: FixedString64Bytes) =
            logInfo $"mimicId: {guid}"
            mimicInit (Guid guid.Value) <| fun fileId ->
                channel.Add $"{Application.dataPath}/../Mirage/{fileId}.mp3"
        mimicId.OnValueChanged <- onMimicIdChanged
    
    override _.OnNetworkDespawn() =
        base.OnDestroy()
        mimicKill <| Guid mimicId.Value.Value