module Mirage.Domain.Audio.Recording

#nowarn "40"

open UnityEngine
open FSharpPlus
open IcedTasks
open System
open System.IO
open System.Collections.Generic
open System.Threading
open System.Buffers
open Mirage.Core.Task.Fork
open Mirage.Core.Audio.Opus.Writer
open Mirage.Core.Audio.PCM
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
let internal deleteRecordings settings =
    forkReturn CancellationToken.None <| fun () -> valueTask {
        if not <| settings.neverDeleteRecordings then
            try Directory.Delete(recordingDirectory, true)
            with | _ -> ()
    }

/// Retrieve all the file names in the recordings directory.
/// Note: This runs on a separate thread, but is not a true non-blocking function, and will cause the other thread to block.
let private getRecordings () =
    forkReturn CancellationToken.None <| fun () -> valueTask {
        return
            try Directory.GetFiles recordingDirectory
            with | _ -> zero
    }

/// Get a recording to be played by a voice mimic.  
/// Not thread-safe. This function is expected to be called within the same thread when using the same __RecordingManager__.
let rec getRecording recordingManager =
    valueTask {
        if recordingManager.recordings.Count = 0 then
            let! recordings = getRecordings()
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

/// Save the given audio samples to Mirage's recording directory.
let saveRecording (fileName: string) (samples: ArraySegment<float32>) format =
    forkReturn CancellationToken.None <| fun () -> valueTask {
        let filePath = Path.Join(recordingDirectory, $"{fileName}.opus")
        let opusWriter =
            OpusWriter
                {   filePath = filePath
                    format = format
                }
        writeOpusSamples opusWriter <|
            {   data = samples.Array
                length = samples.Count
            }
        closeOpusWriter opusWriter
        return filePath
    }

/// Save the audio clip to Mirage's recording directory.
let saveAudioClipWithName fileName (audioClip: AudioClip) =
    valueTask {
        let format =
            {   sampleRate = audioClip.frequency
                channels = audioClip.channels
            }
        let samples = ArrayPool.Shared.Rent audioClip.samples
        try
            ignore <| audioClip.GetData(samples, 0)
            let segment = ArraySegment(samples, 0, audioClip.samples)
            return! saveRecording fileName segment format
        finally
            ArrayPool.Shared.Return samples
    }

/// Save the audio clip to Mirage's recording directory.
/// The file is given a random guid as the file name, and is returned when the function is complete.
let saveAudioClip audioClip = saveAudioClipWithName (Guid.NewGuid().ToString()) audioClip