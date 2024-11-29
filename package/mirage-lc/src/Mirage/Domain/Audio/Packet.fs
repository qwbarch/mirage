module Mirage.Domain.Audio.Packet

open Unity.Netcode
open Mirage.Core.Audio.Opus.Reader

[<Struct>]
type OpusPacket =
    {   mutable opusData: byte[]
        mutable sampleIndex: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) =
            serializer.SerializeValue(&this.opusData)
            serializer.SerializeValue(&this.sampleIndex)

/// Contains all the metadata required for the audio to be parsed properly.
[<Struct>]
type PcmHeader =
    {   mutable totalSamples: int
        mutable sampleRate: int
        mutable channels: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) : unit = 
            serializer.SerializeValue(&this.totalSamples)
            serializer.SerializeValue(&this.sampleRate)
            serializer.SerializeValue(&this.channels)

let PcmHeader (opusReader: OpusReader) =
    {   totalSamples = opusReader.totalSamples
        sampleRate = opusReader.decoder.SampleRate
        channels = opusReader.decoder.NumChannels
    }