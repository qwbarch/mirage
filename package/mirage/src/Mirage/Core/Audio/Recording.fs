module Mirage.Core.Audio.Recording

#nowarn "40"

open FSharpPlus
open Dissonance.Audio
open UnityEngine
open System
open System.IO
open Dissonance
open Mirage.Core.Config

/// The directory to save audio files in.
let private RecordingDirectory = $"{Application.dataPath}/../Mirage"

/// Create a recording file with a random name.
let createRecording format =
    let filePath = $"{RecordingDirectory}/{DateTime.UtcNow.ToFileTime()}.wav"
    let recording = new AudioFileWriter(filePath, format)
    (filePath, recording)

/// Whether or not samples should still be recorded.<br />
/// If false, the recording should be disposed.
let isRecording (dissonance: DissonanceComms) (speechDetected : bool) =
    let isPlayerDead =
        not (isNull GameNetworkManager.Instance)
            && not (isNull GameNetworkManager.Instance.localPlayerController)
            && not GameNetworkManager.Instance.localPlayerController.isPlayerDead
    let pushToTalkEnabled = IngamePlayerSettings.Instance.settings.pushToTalk
    let pushToTalkPressed = pushToTalkEnabled && not dissonance.IsMuted
    let voiceActivated = not pushToTalkEnabled && speechDetected
    isPlayerDead && (pushToTalkPressed || voiceActivated)

/// Delete the recordings of the local player. Any exception found is ignored.
let deleteRecordings () =
    if not <| getLocalConfig().IgnoreRecordingsDeletion.Value then
        try Directory.Delete(RecordingDirectory, true)
        with | _ -> ()

/// Get all the voice recordings' file paths.
let getRecordings () =
    try Directory.GetFiles RecordingDirectory
    with | _ -> zero

/// Get a random recording's file path. If no recordings exist, this will return <b>None</b>.
let getRandomRecording (random: Random) =
    let recordings = getRecordings()
    if recordings.Length = 0 then None
    else Some recordings[random.Next recordings.Length]