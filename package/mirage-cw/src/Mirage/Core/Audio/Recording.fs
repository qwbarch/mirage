module Mirage.Core.Audio.Recording

#nowarn "40"

open FSharpPlus
open UnityEngine
open System
open System.IO
open System.Collections.Generic
open Mirage.Core.Config
open Mirage.Core.Monad

/// The directory to save audio files in.
let internal RecordingDirectory = $"{Application.dataPath}/../Mirage"

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

/// Delete the recordings of the local player. Any exception found is ignored.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let internal deleteRecordings () =
    if not <| getLocalConfig().NeverDeleteVoiceClips.Value then
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