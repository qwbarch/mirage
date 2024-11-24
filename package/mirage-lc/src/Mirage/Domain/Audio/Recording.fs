module Mirage.Domain.Audio.Recording

#nowarn "40"

open FSharpPlus
open System
open System.IO
open System.Collections.Generic
open Mirage.Core.Async.Fork
open Mirage.Domain.Setting
open Mirage.Domain.Logger

type RecordingManager =
    private
        {   /// Populated cache of recording file names, to avoid repeating the same recording.
            recordings: List<string>
            random: Random
            recordingDirectory: string
            mutable lastRecording: option<string>
        }

let mutable private recordingManager =
    {   recordings = new List<string>()
        random = new Random()
        recordingDirectory = null
        lastRecording = None
    }

let initRecordingManager recordingDirectory =
    recordingManager <- 
        {   recordingManager with
                recordingDirectory = recordingDirectory
        }

/// Delete the recordings of the local player. Any exception found is ignored.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let internal deleteRecordings =
    forkReturn <| async {
        if not <| getSettings().neverDeleteRecordings then
            try Directory.Delete(recordingManager.recordingDirectory, true)
            with | _ -> ()
    }

/// Retrieve all the file names in the recordings directory.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let internal getRecordings =
    forkReturn <| async {
        return
            try Directory.GetFiles recordingManager.recordingDirectory
            with | _ -> zero
    }

/// Get a recording to be played by a voice mimic.  
/// Note: Currently not thread-safe due to __lastRecording__. This function should only ever be called from within the same thread.
let rec internal getRecording =
    async {
        if recordingManager.recordings.Count = 0 then
            let! recordings = getRecordings
            recordingManager.recordings.Clear()
            recordingManager.recordings.AddRange recordings
        // Recordings can still be empty.
        if recordingManager.recordings.Count = 0 then return None
        else
            let index = recordingManager.random.Next recordingManager.recordings.Count
            let recording = recordingManager.recordings[index]
            recordingManager.recordings.RemoveAt index
            if not <| recording.EndsWith ".mp3" then
                logWarning $"Found an unsupported recording, make sure it's an mp3 file: {recording}"
                return None
            // In the case where the currently held recordings is reloaded and the pulled recording happens to be
            // the same as the last recording, a new recording is pulled to avoid playing the same recording twice in a row.
            else if Some recording = recordingManager.lastRecording then
                return! getRecording
            else
                recordingManager.lastRecording <- Some recording
                return Some recording
    }