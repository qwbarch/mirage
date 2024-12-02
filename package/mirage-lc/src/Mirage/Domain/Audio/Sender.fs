module Mirage.Domain.Audio.Sender

#nowarn "40"

open System
open System.Threading
open FSharpPlus
open Mirage.Domain.Audio.Stream
open Mirage.Core.Async.Lock
open Mirage.Core.Audio.Wave.Reader
open Mirage.Domain.Audio.Packet
open FSharpx.Control

type AudioSender =
    private
        {   waveReader: WaveReader
            sendPacket: WavePacket -> Unit
            channel: BlockingQueueAgent<ValueOption<WavePacket>>
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
let AudioSender sendPacket waveReader cancellationToken =
    {   waveReader = waveReader
        sendPacket = sendPacket
        channel = new BlockingQueueAgent<ValueOption<WavePacket>>(Int32.MaxValue)
        cancellationToken = cancellationToken
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
        streamAudio sender.waveReader <| fun packet ->
            async {
                let! disposed = isDisposed
                if not disposed then
                    do! sender.channel.AsyncAdd packet
            }
    let rec consumer =
        async {
            do! sender.channel.AsyncGet() >>= function
                | ValueNone -> result <| dispose sender
                | ValueSome packet ->
                    async {
                        let! disposed = isDisposed
                        if not disposed then
                            sender.sendPacket packet
                            do! consumer
                    }
        }
    Async.Start(producer, sender.cancellationToken)
    Async.StartImmediate(consumer, sender.cancellationToken)