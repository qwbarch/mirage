module Mirage.Domain.Audio.Sender

open System
open System.Threading
open FSharpPlus
open FSharp.Control.Tasks.Affine.Unsafe
open Mirage.Core.Audio.Opus.Reader
open Mirage.Core.Ply.Channel
open Mirage.Core.Ply.Lock
open Mirage.Core.Ply.Fork
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Audio.Stream

type AudioSender =
    private
        {   opusReader: OpusReader
            sendPacket: OpusPacket -> Unit
            channel: Channel<ValueOption<OpusPacket>>
            cancellationToken: CancellationToken
            lock: Lock
            mutable disposed: bool
        }
    interface IDisposable with
        member this.Dispose() =
            ignore <| uply {
                let mutable disposed = false
                do! withLock this.lock <| fun () -> uply {
                    disposed <- this.disposed
                    this.disposed <- true
                }
                if not disposed then
                    dispose this.channel
            }

/// Responsible for sending opus audio packets, to be received by a __AudioReceiver__.
let AudioSender sendPacket opusReader cancellationToken =
    {   opusReader = opusReader
        sendPacket = sendPacket
        cancellationToken = cancellationToken
        channel = Channel()
        lock = createLock()
        disposed = false
    }

/// Start broadcasting the audio to all clients.
let startAudioSender sender =
    let isDisposed () =
        withLock sender.lock <| fun () -> uply {
            return sender.disposed
        }
    let producer () =
        streamAudio sender.opusReader sender.cancellationToken <| fun packet ->
            uply {
                let! disposed = isDisposed()
                if not disposed then
                    writeChannel sender.channel packet
            }
    let rec consumer () =
        uply {
            let! value = readChannel sender.channel sender.cancellationToken
            match value with
                | ValueNone -> dispose sender
                | ValueSome packet ->
                    let! disposed = isDisposed()
                    if not disposed then
                        sender.sendPacket packet
                        do! consumer()
        }
    fork producer sender.cancellationToken
    ignore <| consumer()