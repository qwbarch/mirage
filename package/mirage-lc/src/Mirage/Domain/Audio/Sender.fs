module Mirage.Domain.Audio.Sender

open System
open System.Threading
open FSharpPlus
open IcedTasks
open Mirage.Core.Audio.Opus.Reader
open Mirage.Domain.Audio.Packet
open Mirage.Domain.Audio.Stream
open Mirage.Core.Task.Channel
open Mirage.Core.Task.Lock
open Mirage.Core.Task.LVar
open Mirage.Core.Task.Utility
open Mirage.Core.Task.Fork

type AudioSender =
    private
        {   opusReader: OpusReader
            sendPacket: OpusPacket -> Unit
            channel: Channel<ValueOption<OpusPacket>>
            cancellationToken: CancellationToken
            lock: Lock
            running: LVar<bool>
        }
    interface IDisposable with
        member this.Dispose() =
            ignore <| writeLVar this.running false

/// Responsible for sending opus audio packets, to be received by a __AudioReceiver__.
let AudioSender sendPacket opusReader cancellationToken =
    {   opusReader = opusReader
        sendPacket = sendPacket
        cancellationToken = cancellationToken
        channel = Channel cancellationToken
        lock = createLock()
        running = newLVar true
    }

/// Start broadcasting the audio to all clients.
let startAudioSender sender =
    let producer () =
        streamAudio sender.opusReader sender.cancellationToken <| fun packet ->
            valueTask {
                let! running = readLVar sender.running
                if running then
                    writeChannel sender.channel packet
            }
    let consumer () =
        forever <| fun () -> valueTask {
            let! value = readChannel sender.channel
            match value with
                | ValueNone -> dispose sender
                | ValueSome packet ->
                    let! running = readLVar sender.running
                    if running then
                        sender.sendPacket packet
        }
    fork sender.cancellationToken producer
    ignore <| consumer()