module Mirage.Domain.Audio.Packet

open Unity.Netcode

[<Struct>]
type OpusPacket =
    {   mutable opusData: byte[]
        mutable sampleIndex: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) =
            serializer.SerializeValue(&this.opusData)
            serializer.SerializeValue(&this.sampleIndex)