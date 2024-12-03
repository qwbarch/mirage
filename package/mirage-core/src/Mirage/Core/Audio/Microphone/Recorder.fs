module Mirage.Core.Audio.Microphone.Recorder

open System
open System.IO
open FSharpPlus
open FSharp.Control.Tasks.Affine.Unsafe
open Mirage.Core.Audio.Microphone.Detection
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Audio.Opus.Writer
open Mirage.Core.Ply.Channel
open Mirage.Core.Ply.Fork

//[<Struct>]
//type RecordStart =
//    {   originalFormat: WaveFormat
//        resampledFormat: WaveFormat
//    }
//
//[<Struct>]
//type RecordFound =
//    {   //vadFrame: VADFrame
//        fullAudio: ResampledAudio
//        currentAudio: ResampledAudio
//    }
//
///// Note: After the callback finishes for this action, the mp3 writer is disposed.
//[<Struct>]
//type RecordEnd =
//    {   opusWriter: OpusWriter
//        //vadFrame: VADFrame
//        //vadTimings: list<VADFrame>
//        fullAudio: ResampledAudio
//        currentAudio: ResampledAudio
//        audioDurationMs: int
//    }
//
///// A sum type representing the progress of a recording.
//[<Struct>]
//type RecordAction
//    = RecordStart of recordStart: RecordStart
//    | RecordFound of recordFound: RecordFound
//    | RecordEnd of recordEnd: RecordEnd

/// Records audio from a live microphone feed.
type Recorder<'State> =
    private { channel: Channel<ValueTuple<'State, DetectAction>> }
    interface IDisposable with
        member this.Dispose() = dispose this.channel

type RecorderArgs<'State> =
    {   /// Minimum amount of audio duration that a recording should contain. If the minimum isn't met, the recording is not written to disk.
        minAudioDurationMs: int
        /// Directory to write recordings to.
        directory: string
        /// Whether recordings should be created or not, based on the current state.
        allowRecordVoice: 'State -> bool
        ///// Function that gets called every time a recording is in the process of being created.
        //onRecording: 'State -> RecordAction -> Async<Unit>
    }

let Recorder args =
    let channel = Channel()
    let rec consumer () =
        uply {
            let! struct (state, action) = readChannel' channel
            //let onRecording = args.onRecording state
            match action with
                | DetectStart _ -> ()
                    //do! onRecording << RecordStart <|
                    //    {   originalFormat = payload.originalFormat
                    //        resampledFormat = payload.resampledFormat
                    //    }
                | DetectEnd payload ->
                    if payload.audioDurationMs >= args.minAudioDurationMs && args.allowRecordVoice state then
                        let opusWriter =
                            OpusWriter 
                                {   filePath = Path.Join(args.directory, $"{Guid.NewGuid()}.opus")
                                    format = payload.fullAudio.original.format
                                }
                        writeOpusSamples opusWriter payload.fullAudio.original.samples
                        closeOpusWriter opusWriter
                        //do! onRecording << RecordEnd <|
                        //    {   opusWriter = opusWriter
                        //        //vadFrame = payload.vadFrame
                        //        //vadTimings = payload.vadTimings
                        //        fullAudio = payload.fullAudio
                        //        currentAudio = payload.currentAudio
                        //        audioDurationMs = payload.audioDurationMs
                        //    }
                | DetectFound _ -> ()
                    //do! onRecording << RecordFound <|
                    //    {   //vadFrame = payload.vadFrame
                    //        fullAudio = payload.fullAudio
                    //        currentAudio = payload.currentAudio
                    //    }
            do! consumer()
        }
    fork' consumer
    { channel = channel }

let writeRecorder recorder = writeChannel recorder.channel