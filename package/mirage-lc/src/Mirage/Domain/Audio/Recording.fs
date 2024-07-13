module Mirage.Domain.Audio.Recording

#nowarn "40"

open FSharpPlus
open UnityEngine
open System
open System.IO
open System.Collections.Generic
open BepInEx
open Mirage.Core.Async.Fork

/// The directory to save audio files in.
let private RecordingDirectory = $"{Application.dataPath}/../Mirage/Recording"

type RecordingManager =
    private
        {   /// Populated cache of recording file names, to avoid repeating the same recording.
            recordings: List<string>
            random: Random
        }

let RecordingManager () =
    {   recordings = new List<string>()
        random = new Random()
    }

///// Whether or not samples should still be recorded.<br />
///// If false, the recording should be disposed.
//let internal isRecording (dissonance: DissonanceComms) (speechDetected: bool) =
//    let isPlayerDead =
//        not (isNull GameNetworkManager.Instance)
//            && not (isNull GameNetworkManager.Instance.localPlayerController)
//            && not GameNetworkManager.Instance.localPlayerController.isPlayerDead
//    let pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
//    let pushToTalkPressed = pushToTalkEnabled && not dissonance.IsMuted
//    let voiceActivated = not pushToTalkEnabled && speechDetected
//    isPlayerDead && (pushToTalkPressed || voiceActivated)

/// Delete the recordings of the local player. Any exception found is ignored.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let internal deleteRecordings () =
    //if not <| getLocalConfig().IgnoreRecordingsDeletion.Value then
    try Directory.Delete(RecordingDirectory, true)
    with | _ -> ()

/// Retrieve all the file names in the recordings directory.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let internal getRecordings (info: PluginInfo) =
    forkReturn <| async {
        let directory =
            Path.Join(
                Path.GetDirectoryName(info.Location).AsSpan(),
                "../../Mirage/Recording"
            )
        return
            try Directory.GetFiles directory
            with | _ -> zero
    }

/// TODO: REMVOE THIS
let private getRecordings2 =
    forkReturn <| async {
        return
            try Directory.GetFiles RecordingDirectory
            with | _ -> zero
    }

/// Get a recording's file name, depending on the configuration's current imitation mode.
let internal getRecording manager =
    async {
        if manager.recordings.Count = 0 then
            let! recordings = getRecordings2
            manager.recordings.Clear()
            manager.recordings.AddRange recordings
        return
            // Recordings can still be empty.
            if manager.recordings.Count = 0 then None
            else
                let index = manager.random.Next manager.recordings.Count
                let recording = manager.recordings[index]
                manager.recordings.RemoveAt index
                Some recording
    }