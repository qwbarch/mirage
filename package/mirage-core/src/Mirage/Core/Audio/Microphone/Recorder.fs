module Mirage.Core.Audio.Microphone.Recorder

open IcedTasks
open System
open System.IO
open System.Threading
open System.Buffers
open FSharpPlus
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Opus.Writer
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Fork
open Mirage.Core.Task.Loop

/// Records audio from a live microphone feed.
type Recorder<'State> = { channel: Channel<ValueTuple<'State, DetectAction>> }

type RecorderArgs<'State> =
    {   /// Minimum amount of audio duration that a recording should contain. If the minimum isn't met, the recording is not written to disk.
        minAudioDurationMs: int
        /// Directory to write recordings to.
        directory: string
        /// Whether recordings should be created or not, based on the current state.
        allowRecordVoice: 'State -> bool
    }

let Recorder args =
    let channel = Channel CancellationToken.None
    let consumer () =
        forever <| fun () -> valueTask {
            let! struct (state, action) = readChannel channel
            match action with
                | DetectStart _ -> ()
                | DetectEnd payload ->
                    if payload.audioDurationMs >= args.minAudioDurationMs && args.allowRecordVoice state then
                        let guid = Guid.NewGuid()
                        let opusWriter =
                            OpusWriter 
                                {   filePath = Path.Join(args.directory, $"{guid}.opus")
                                    format = payload.fullAudio.original.format
                                }
                        try
                            writeOpusSamples opusWriter payload.fullAudio.original.samples
                            closeOpusWriter opusWriter
                        finally
                            ArrayPool.Shared.Return payload.fullAudio.original.samples.data
                            ArrayPool.Shared.Return payload.fullAudio.resampled.samples.data
                            dispose opusWriter
        }
    fork CancellationToken.None consumer
    { channel = channel }

let inline writeRecorder recorder = writeChannel recorder.channel