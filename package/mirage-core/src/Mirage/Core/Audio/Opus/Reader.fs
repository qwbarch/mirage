module Mirage.Core.Audio.Opus.Reader

open System
open System.IO
open FSharpPlus
open Concentus
open Concentus.Structs
open Concentus.Oggfile
open Mirage.Prelude
open Mirage.Core.Async.Fork
open Mirage.Core.Audio.Opus.Codec

type OpusReader =
    {   reader: OpusOggReadStream
        decoder: IOpusDecoder
        totalSamples: int
    }
    interface IDisposable with
        member this.Dispose() = 
            dispose this.decoder

/// Reads an opus file from a background thread, and then returns it to the caller.
let readOpusFile filePath =
    forkReturn <| async {
        use stream = new FileStream(filePath, FileMode.Open, FileAccess.Read)
        let decoder = OpusDecoder()
        let reader = OpusOggReadStream(null, stream)
        let mutable totalSamples = 0
        let mutable packet = reader.ReadNextRawPacket()
        while not <| isNull packet do
            &totalSamples += OpusPacketInfo.GetNumSamples(packet.AsSpan(), decoder.SampleRate)
        return {
            reader = OpusOggReadStream(null, stream)
            decoder = decoder
            totalSamples = totalSamples
        }
    }