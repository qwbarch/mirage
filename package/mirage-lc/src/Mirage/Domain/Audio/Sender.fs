module Mirage.Domain.Audio.Sender

open System
open FSharpx.Control
open Mirage.Core.Audio.File.OpusReader
open Mirage.Domain.Audio.Packet
open Mirage.Core.Async.LVar
open FSharpPlus
open Mirage.Core.Async.Lock

/// Responsible for sending opus audio packets, to be received by a __AudioReceiver__.
type AudioSender =
    private
        {   sendPacket: OpusPacket -> unit
            opusReader: OpusReader
            channel: BlockingQueueAgent<ValueOption<OpusPacket>>
            mutable disposed: LVar<bool>
        }
    interface IDisposable with
        member this.Dispose() =
            Async.StartImmediate <| async {
                do! modifyLVar this.disposed <| fun disposed ->
                    if not disposed then
                        dispose this.opusReader
                        dispose this.channel
                        dispose this.disposed
                    true
            }

type AudioSenderArgs =
    {   /// Function to run whenever a packet should be sent.
        sendPacket: OpusPacket -> unit
        opusReader: OpusReader
    }

let AudioSender args =
    {   sendPacket = args.sendPacket
        opusReader = args.opusReader
        channel = new BlockingQueueAgent<ValueOption<OpusPacket>>(Int32.MaxValue)
        disposed = newLVar false
    }

let startAudioSender audioSender =
    ()