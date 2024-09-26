module Mirage.Domain.Audio.Recording

open FSharpPlus
open System
open System.IO
open System.Collections.Generic
open Mirage.Core.Async.Fork
open Mirage.Domain.Setting

type RecordingManager =
    private
        {   /// Populated cache of recording file names, to avoid repeating the same recording.
            recordings: List<string>
            random: Random
            recordingDirectory: string
        }

let mutable private recordingManager =
    {   recordings = new List<string>()
        random = new Random()
        recordingDirectory = null
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
let internal getRecording =
    async {
        if recordingManager.recordings.Count = 0 then
            let! recordings = getRecordings
            recordingManager.recordings.Clear()
            recordingManager.recordings.AddRange recordings
        return
            // Recordings can still be empty.
            if recordingManager.recordings.Count = 0 then None
            else
                let index = recordingManager.random.Next recordingManager.recordings.Count
                let recording = recordingManager.recordings[index]
                recordingManager.recordings.RemoveAt index
                Some recording
    }