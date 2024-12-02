module Mirage.Domain.Audio.Packet

open Unity.Netcode
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Wave.Reader

[<Struct>]
type WavePacket =
    {   mutable pcmData: PCMData
        mutable sampleIndex: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) =
            serializer.SerializeValue(&this.pcmData)
            serializer.SerializeValue(&this.sampleIndex)

/// Contains all the metadata required for the audio to be parsed properly.
[<Struct>]
type WaveHeader =
    {   mutable lengthSamples: int
        mutable channels: int
        mutable frequency: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) : unit = 
            serializer.SerializeValue(&this.lengthSamples)
            serializer.SerializeValue(&this.channels)
            serializer.SerializeValue(&this.frequency)

let WaveHeader waveReader =
    let reader = waveReader.reader
    {   lengthSamples = int reader.SampleCount
        channels = reader.WaveFormat.Channels
        frequency = reader.WaveFormat.SampleRate
    }