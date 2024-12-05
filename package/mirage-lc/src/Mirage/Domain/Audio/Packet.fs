module Mirage.Domain.Audio.Packet

open Unity.Netcode
open System.Buffers

[<Struct>]
type OpusPacket =
    {   mutable opusLength: int
        mutable opusData: byte[]
        mutable sampleIndex: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) =
            if serializer.IsReader then
                let reader = serializer.GetFastBufferReader()
                reader.ReadValueSafe &this.opusLength
                this.opusData <- ArrayPool.Shared.Rent this.opusLength
                reader.ReadBytesSafe(&this.opusData, this.opusLength)
                reader.ReadValueSafe &this.sampleIndex
            else
                let writer = serializer.GetFastBufferWriter()
                writer.WriteValue &this.opusLength
                writer.WriteBytes(this.opusData, this.opusLength)
                writer.WriteValue &this.sampleIndex