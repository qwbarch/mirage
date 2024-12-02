module Mirage.Domain.Audio.Recording

#nowarn "40"

open FSharpPlus
open System
open System.IO
open System.Collections.Generic
open Mirage.Core.Async.Fork
open Mirage.Domain.Setting
open Mirage.Domain.Logger
open Mirage.Domain.Directory

/// For retrieving player recordings, avoiding grabbing the same recording in succession.
/// Despite functions using RecordManager being async, this state is not thread-safe.
type RecordingManager =
    private
        {   random: Random
            /// Populated cache of recording file names, to avoid repeating the same recording.
            recordings: List<string>
            mutable recordingCount: int
            mutable lastRecording: option<string>
        }

let RecordingManager () =
    {   random = Random()
        recordings = zero
        recordingCount = zero
        lastRecording = zero
    }

/// Delete the recordings of the local player. Any exception found is ignored.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let internal deleteRecordings =
    forkReturn <| async {
        if not <| getSettings().neverDeleteRecordings then
            try Directory.Delete(recordingDirectory, true)
            with | _ -> ()
    }

/// Retrieve all the file names in the recordings directory.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let private getRecordings =
    forkReturn <| async {
        return
            try Directory.GetFiles recordingDirectory
            with | _ -> zero
    }

/// Get a recording to be played by a voice mimic.  
/// Not thread-safe. This function is expected to be called within the same thread when using the same __RecordingManager__.
let rec getRecording recordingManager =
    async {
        if recordingManager.recordings.Count = 0 then
            let! recordings = getRecordings
            recordingManager.recordings.Clear()
            recordingManager.recordings.AddRange recordings
            recordingManager.recordingCount <- recordings.Length
        // Recordings can still be empty.
        if recordingManager.recordings.Count = 0 then return None
        else
            let index = recordingManager.random.Next recordingManager.recordings.Count
            let recording = recordingManager.recordings[index]
            recordingManager.recordings.RemoveAt index
            if not <| recording.EndsWith ".opus" then
                logWarning $"Found an unsupported recording, make sure it's an opus file: {recording}"
                return None
            // In the case where the currently held recordings is reloaded and the pulled recording happens to be
            // the same as the last recording, a new recording is pulled to avoid playing the same recording twice in a row.
            else if Some recording = recordingManager.lastRecording && recordingManager.recordingCount > 1 then
                return! getRecording recordingManager
            else
                recordingManager.lastRecording <- Some recording
                return Some recording
    }