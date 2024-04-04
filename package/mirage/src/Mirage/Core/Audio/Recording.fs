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
open Mirage.Core.Logger

/// The directory to save audio files in.
let private RecordingDirectory = $"{Application.dataPath}/../Mirage"

type RecordingManager =
    private
        {   /// Populated cache of recording file names.
            /// This is only used when imitation mode is set to <b>ImitateNoRepeat</b>.
            recordings: List<string>
            random: Random
        }

let RecordingManager () =
    {   recordings = new List<string>()
        random = new Random()
    }

/// Create a recording file with a random name.
let internal createRecording format =
    let filePath = Path.Join(RecordingDirectory, $"{DateTime.UtcNow.ToFileTime()}.wav")
    let recording = new AudioFileWriter(filePath, format)
    (filePath, recording)

/// Whether or not samples should still be recorded.<br />
/// If false, the recording should be disposed.
let internal isRecording (dissonance: DissonanceComms) (speechDetected: bool) =
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
let internal deleteRecordings () =
    if not <| getLocalConfig().IgnoreRecordingsDeletion.Value then
        try Directory.Delete(RecordingDirectory, true)
        with | _ -> ()

/// Retrieve all the file names in the recordings directory.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let private getRecordings =
    forkReturn <| async {
        return
            try Directory.GetFiles RecordingDirectory
            with | _ -> zero
        }

/// Get a recording's file name, depending on the configuration's current imitation mode.
let internal getRecording manager =
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
                logInfo $"recordings: {manager.recordings.Count}"
                return
                    // Recordings can still be empty.
                    if manager.recordings.Count = 0 then None
                    else
                        let index = manager.random.Next manager.recordings.Count
                        logInfo $"index: {index}"
                        let recording = manager.recordings[index]
                        manager.recordings.RemoveAt index
                        logInfo $"recordings size after removed: {manager.recordings.Count}"
                        Some recording
        }