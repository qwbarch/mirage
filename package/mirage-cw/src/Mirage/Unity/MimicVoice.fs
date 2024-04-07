module Mirage.Unity.MimicVoice

open FSharpPlus
open UnityEngine
open Mirage.Core.Field
open Mirage.Unity.AudioStream
open System.IO
open RpcBehaviour
open Mirage.Core.Logger

let mutable internal PlaybackPrefab: GameObject = null

type MimicVoice() =
    inherit RpcBehaviour()

    let Playback = field()

    override this.Start() =
        base.Start()
        let playback = Object.Instantiate<GameObject> PlaybackPrefab
        playback.transform.parent <- this.transform
        set Playback playback
        playback.SetActive true
        let audioStream = this.GetComponent<AudioStream>()
        audioStream.SetAudioSource <| playback.GetComponent<AudioSource>()
        
        let filePath = $"{Application.dataPath}/../Mirage/speech.wav"
        logInfo $"filePath: {filePath}"
        logInfo $"isHost: {this.IsHost}"
        logInfo $"File.Exists: {File.Exists filePath}"
        if this.IsHost && File.Exists filePath then
            audioStream.StreamAudioFromFile filePath

    member this.LateUpdate() =
        // Update the playback component to always be on the same position as the parent.
        // This ensures audio plays from the correct position.
        flip iter Playback.Value <| fun playback ->
        playback.transform.position <- this.transform.position