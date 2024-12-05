module Mirage.Core.Audio.Opus.Reader

open System
open System.IO
open System.Threading
open System.Text
open Concentus.Oggfile
open IcedTasks
open Mirage.Prelude
open Mirage.Core.Audio.Opus.Codec
open Mirage.Core.Task.Fork

/// Based on __OpusOggStream__, avoiding array allocations where possible.  
/// Credits: https://github.com/lostromb/concentus.oggfile
type OpusReader =
    private
        {   mutable stream: Stream
            mutable packetProvider: IPacketProvider
            mutable endOfStream: bool
            mutable nextDataPacket: byte[]
            mutable nextDataPacketLength: int
            mutable granuleCount: int64
            mutable pageCount: int
            mutable pageGranulePosition: int64
            mutable pagePosition: int
            mutable tags: OpusTags
            mutable totalSamples: int
        }

let rec private queueNextPacket this =
    if not this.endOfStream then
        let packet = this.packetProvider.GetNextPacket()
        if isNull packet then
            this.endOfStream <- true
            this.nextDataPacketLength <- 0
        else
            this.pageGranulePosition <- packet.PageGranulePosition
            this.pagePosition <- packet.PageSequenceNumber

            //let buffer = ArrayPool.Shared.Rent packet.Length
            let buffer = Array.zeroCreate<byte> packet.Length
            ignore <| packet.Read(buffer, 0, packet.Length)
            packet.Done()

            let header = Encoding.UTF8.GetString(buffer, 0, 8)
            if packet.Length > 8 && header.Equals "OpusHead" then
                //ArrayPool.Shared.Return buffer
                queueNextPacket this
            else if buffer.Length > 8 && header.Equals "OpusTags" then
                //ArrayPool.Shared.Return buffer
                this.tags <- OpusTags.ParsePacket(buffer, packet.Length)
                queueNextPacket this
            else
                //ArrayPool.Shared.Return <| this.nextDataPacket
                this.nextDataPacket <- buffer
                this.nextDataPacketLength <- packet.Length

let private initialize this =
    let oggContainerReader = new OggContainerReader(this.stream, true)
    if not <| oggContainerReader.Init() then
        false
    else if oggContainerReader.StreamSerials.Length = 0 then
        false
    else
        this.packetProvider <-
            oggContainerReader.GetStream
                <| oggContainerReader.StreamSerials[0]
        if this.stream.CanSeek then
            this.granuleCount <- this.packetProvider.GetGranuleCount() 
            this.pageCount <- this.packetProvider.GetTotalPageCount()
        queueNextPacket this
        true

let hasNextPacket opusReader = not opusReader.endOfStream

[<Struct>]
type RawPacket =
    {   packet: byte[]
        packetLength: int
    }

/// Read the next opus packet.  
/// This internally uses an ArrayPool to avoid allocations.  
/// When __readNextRawPacket__ is called, the previous packet is returned to the ArrayPool.  
/// After __readNextRawPacket__ is called, you must never reference the previous packet.
let readNextRawPacket opusReader =
    if opusReader.nextDataPacketLength = 0 then
        opusReader.endOfStream <- true
        { packet = null; packetLength = 0 }
    else
        let packet = opusReader.nextDataPacket
        let packetLength = opusReader.nextDataPacketLength
        queueNextPacket opusReader
        { packet = packet; packetLength = packetLength }

let getCurrentTime opusReader =
    TimeSpan.FromSeconds <| float opusReader.pageGranulePosition / float OpusSampleRate

let getTotalSamples = _.totalSamples

/// Reads an opus file from a background thread, and then returns it to the caller.
let readOpusFile filePath =
    forkReturn CancellationToken.None <| fun () -> valueTask {
        let! bytes = File.ReadAllBytesAsync filePath
        let stream = new MemoryStream(bytes)
        let this =
            {   stream = stream
                packetProvider = null
                endOfStream = false
                nextDataPacket = null
                nextDataPacketLength = 0
                granuleCount = 0
                pageCount = 0
                pageGranulePosition = 0
                pagePosition = 0
                tags = null
                totalSamples = 0
            }
        this.endOfStream <- not <| initialize this
        
        let mutable packets = 0
        while hasNextPacket this do
            ignore <| readNextRawPacket this
            &packets += 1
        this.totalSamples <- packets * SamplesPerPacket
        //stream.Position <- 0

        this.stream <- new MemoryStream(bytes)
        this.packetProvider <- null
        this.nextDataPacket <- null
        this.nextDataPacketLength <- 0
        this.granuleCount <- 0
        this.pageCount <- 0
        this.pageGranulePosition <- 0
        this.pagePosition <- 0
        this.tags <- null
        this.endOfStream <- not <| initialize this

        return this
    }