module Mirage.Core.Audio.Recording

#nowarn "40"

open FSharpPlus
open Dissonance
open Dissonance.Audio
open UnityEngine
open System
open System.IO
open System.Collections.Generic
open Mirage.Core.Config
open Mirage.Core.Monad

/// The directory to save audio files in.
let private RecordingDirectory = $"{Application.dataPath}/../Mirage"

type RecordingManager =
    private
        {   directory: string
            /// Populated cache of recording file names.
            /// This is only used when imitation mode is set to <b>ImitateNoRepeat</b>.
            recordings: List<string>
            random: Random
        }

let private manager =
    {   directory = RecordingDirectory
        recordings = new List<string>()
        random = new Random()
    }

/// Create a recording file with a random name.
let createRecording format =
    let filePath = Path.Join(manager.directory, $"{DateTime.UtcNow.ToFileTime()}.wav")
    let recording = new AudioFileWriter(filePath, format)
    (filePath, recording)

/// Whether or not samples should still be recorded.<br />
/// If false, the recording should be disposed.
let isRecording (dissonance: DissonanceComms) (speechDetected: bool) =
    let isPlayerDead =
        not (isNull GameNetworkManager.Instance)
            && not (isNull GameNetworkManager.Instance.localPlayerController)
            && not GameNetworkManager.Instance.localPlayerController.isPlayerDead
    let pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
    let pushToTalkPressed = pushToTalkEnabled && not dissonance.IsMuted
    let voiceActivated = not pushToTalkEnabled && speechDetected
    isPlayerDead && (pushToTalkPressed || voiceActivated)

/// Delete the recordings of the local player. Any exception found is ignored.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let deleteRecordings () =
    Async.StartImmediate <|
        async {
            if not <| getLocalConfig().IgnoreRecordingsDeletion.Value then
                try
                    return! forkReturn <| async {
                        try Directory.Delete(manager.directory, true)
                        with _ -> ()
                    }
                finally
                    manager.recordings.Clear()
        }

/// Retrieve all the file names in the recordings directory.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let private getRecordings =
    forkReturn <| async {
        return
            try Directory.GetFiles manager.directory
            with | _ -> zero
        }

/// Get a recording's file name, depending on the configuration's current imitation mode.
let internal getRecording =
    async {
        match getConfig().imitateMode with
            | ImitateRandom ->
                let! recordings = getRecordings
                return 
                    if recordings.Length = 0 then None
                    else Some recordings[manager.random.Next recordings.Length]
            | ImitateNoRepeat ->
                if manager.recordings.Count = 0 then
                    let! recordings = getRecordings
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