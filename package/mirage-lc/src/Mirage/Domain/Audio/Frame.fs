module Mirage.Domain.Audio.Frame

open System
open System.IO
open NAudio.Wave
open FSharpPlus
open Unity.Netcode
open Mirage.Core.Audio.File.Mp3Reader

/// Represents a compressed audio frame, and the sample index it begins at, after decompressing the frame.
[<Struct>]
type FrameData =
    {   mutable rawData: byte[]
        mutable sampleIndex: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) =
            serializer.SerializeValue(&this.rawData)
            serializer.SerializeValue(&this.sampleIndex)

/// Contains all the metadata required for the audio to be parsed properly.
[<Struct>]
type PcmHeader =
    {   mutable samples: int
        mutable channels: int
        mutable frequency: int
        mutable blockSize: int
        mutable bitRate: int
    }
    interface INetworkSerializable with
        member this.NetworkSerialize(serializer: BufferSerializer<'T>) : unit = 
            serializer.SerializeValue(&this.samples)
            serializer.SerializeValue(&this.channels)
            serializer.SerializeValue(&this.frequency)
            serializer.SerializeValue(&this.blockSize)
            serializer.SerializeValue(&this.bitRate)

/// Creates a <b>PcmHeader</b> using the given <b>Mp3Reader</b>.
let PcmHeader (mp3Reader: Mp3Reader) =
    let reader = mp3Reader.reader
    {   samples = int reader.totalSamples
        channels = reader.Mp3WaveFormat.Channels
        frequency = reader.Mp3WaveFormat.SampleRate
        blockSize  = int reader.Mp3WaveFormat.blockSize
        bitRate = reader.Mp3WaveFormat.AverageBytesPerSecond * 8
    }

/// <summary>
/// Converts the given MP3 frame data to PCM format.
/// Note: This function <i>will</i> throw an exception if invalid bytes are provided.
/// </summary>
let decompressFrame (decompressor: IMp3FrameDecompressor) (frameData: array<byte>) : array<float32> =
    use stream = new MemoryStream(frameData)
    let frame = Mp3Frame.LoadFromStream stream
    let samples = Array.zeroCreate <| 16384 * 4 // Large enough buffer for a single frame.
    let bytesDecompressed = decompressor.DecompressFrame(frame, samples, 0)
    let pcmData : array<int16> = Array.zeroCreate bytesDecompressed
    Buffer.BlockCopy(samples, 0, pcmData, 0, bytesDecompressed)
    flip (/) 32768.0f << float32 <!> pcmData