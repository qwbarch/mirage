module Mirage.Domain.Audio.Sender

open System
open System.Threading
open FSharpPlus
open FSharpx.Control
open Mirage.Domain.Audio.Frame
open Mirage.Domain.Audio.Stream
open Mirage.Core.Audio.File.Mp3Reader

/// Send audio to all <b>AudioReceiver</b>s.
type AudioSender =
    private
        {   sendFrame: FrameData -> unit
            mp3Reader: Mp3Reader
            channel: BlockingQueueAgent<Option<FrameData>>
            canceller: CancellationTokenSource
            mutable disposed: bool
        }
    interface IDisposable with
        member this.Dispose() =
            if not this.disposed then
                this.disposed <- true
                this.canceller.Cancel()
                dispose this.canceller
                dispose this.mp3Reader
                dispose this.channel

/// <summary>
/// Start the audio sender. This does not begin broadcasting audio.
/// </summary>
/// <param name="sendFrame">
/// The RPC method for sending frame data to all clients.
/// </param>
/// <param name="filePath">
/// Source audio to stream from, supporting only <b>.wav</b> audio files.
/// </param>
let AudioSender sendFrame waveReader =
    let sender =
        {   sendFrame = sendFrame
            mp3Reader = waveReader
            channel = new BlockingQueueAgent<Option<FrameData>>(Int32.MaxValue)
            canceller = new CancellationTokenSource()
            disposed = false
        }
    sender

let sendAudio sender =
    // The "producer" processes the audio frames from a separate thread, and passes it onto the consumer.
    let producer = streamAudio sender.mp3Reader sender.channel.AsyncAdd

    // The "consumer" reads the processed audio frames and then runs the sendFrame function.
    let rec consumer =
        async {
            let mutable running = true
            while running do
                let! frameData = sender.channel.AsyncGet()
                match frameData with
                    | None ->
                        running <- false
                        dispose sender
                    | Some frame -> sender.sendFrame frame
        }

    // Start the producer on a separate thread.
    Async.Start(producer, sender.canceller.Token)

    // Start the consumer in the current thread.
    Async.StartImmediate(consumer, sender.canceller.Token)