module Mirage.Domain.Audio.Sender

#nowarn "40"

open System
open System.Threading
open FSharpPlus
open FSharpx.Control
open Mirage.Core.Audio.Opus.Reader
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Audio.Stream
open Mirage.Core.Async.Lock

type AudioSender =
    private
        {   opusReader: OpusReader
            sendPacket: OpusPacket -> Unit
            channel: BlockingQueueAgent<Option<OpusPacket>>
            cancellationToken: CancellationToken
            lock: Lock
            mutable disposed: bool
        }
    interface IDisposable with
        member this.Dispose() =
            let mutable disposed = false
            Async.StartImmediate << withLock' this.lock <| async {
                disposed <- this.disposed
                this.disposed <- true
            }
            if not disposed then
                dispose this.channel

/// Responsible for sending opus audio packets, to be received by a __AudioReceiver__.
let AudioSender sendPacket opusReader cancellationToken =
    {   opusReader = opusReader
        sendPacket = sendPacket
        cancellationToken = cancellationToken
        channel = new BlockingQueueAgent<Option<OpusPacket>>(Int32.MaxValue)
        lock = createLock()
        disposed = false
    }

/// Start broadcasting the audio to all clients.
let startAudioSender sender =
    let isDisposed =
        withLock' sender.lock <| async {
            return sender.disposed
        }
    let producer =
        streamAudio sender.opusReader <| fun packet ->
            async {
                let! disposed = isDisposed
                if not disposed then
                    do! sender.channel.AsyncAdd packet
            }
    let rec consumer =
        async {
            do! sender.channel.AsyncGet() >>= function
                | None -> result <| dispose sender
                | Some packet ->
                    async {
                        let! disposed = isDisposed
                        if not disposed then
                            sender.sendPacket packet
                            do! consumer
                    }
        }
    Async.Start(producer, sender.cancellationToken)
    Async.StartImmediate(consumer, sender.cancellationToken)