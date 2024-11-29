module Mirage.Core.Audio.File.OpusReader

open Concentus.Oggfile
open System
open System.IO
open FSharpPlus
open Concentus
open Mirage.Prelude
open Mirage.Core.Async.Fork
open Concentus.Structs

type OpusReader =
    {   reader: OpusOggReadStream
        decoder: IOpusDecoder
        totalSamples: int
    }
    interface IDisposable with
        member this.Dispose() = 
            dispose this.decoder

/// Decoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let createOpusDecoder () = OpusCodecFactory.CreateDecoder(48_000, 1)

/// Reads an opus file from a background thread, and then returns it to the caller.
let readOpusFile filePath =
    forkReturn <| async {
        use stream = new FileStream(filePath, FileMode.Open, FileAccess.Read)
        let decoder = createOpusDecoder()
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