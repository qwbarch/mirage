module Mirage.App.Foo

open System.Collections.Generic
open Mirage.Core.Audio.Opus.Reader
open Mirage.Core.Audio.Opus.Codec
open Concentus.Structs
open System
open Mirage.Core.Audio.Opus
open Ply
open Mirage.Core.Ply.Utility
open System.Threading.Tasks
open FSharp.Control.Tasks

[<EntryPoint>]
let main args =
    ignore <| vtask {
        while true do
            do! Task.Delay(100)
            printfn "hello world!"
    }
    ignore <| Console.ReadLine()
    //Async.RunSynchronously <| async {
    //    let! opusReader = readOpusFile "hello.opus"
    //    let frames = List<byte>()
    //    let mutable frame = opusReader.reader.ReadNextRawPacket()

    //    printfn $"total samples: {opusReader.totalSamples}" 
    //    let decoder = Codec.OpusDecoder()
    //    
    //    while not <| isNull frame do
    //        let frameSamples = OpusPacketInfo.GetNumSamples(frame.AsSpan(), OpusSampleRate)
    //        printfn $"frameSamples: {frameSamples}"
    //        let samples = Array.zeroCreate<float32> <| frameSamples * OpusChannels
    //        let decodedLength = decoder.Decode(frame, samples, samples.Length)
    //        if decodedLength <= 0 then
    //            printfn $"decodedLength: {decodedLength}"
    //        
    //        frame <- opusReader.reader.ReadNextRawPacket()
    //    
    //    printfn "done"
    //}

    0