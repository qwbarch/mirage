module Mirage.Core.Audio.Opus.Codec

#nowarn "44"

open OpusDotNet
open Concentus.Enums

let [<Literal>] OpusSampleRate = 48_000
let [<Literal>] OpusChannels = 1
let [<Literal>] FrameSizeMs = 20
let [<Literal>] SamplesPerPacket = OpusSampleRate * FrameSizeMs / 1000
let [<Literal>] PacketPcmLength = SamplesPerPacket * 2

/// Decoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let OpusDecoder () = new OpusDecoder(FrameSizeMs, OpusSampleRate, OpusChannels)

/// Encoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let OpusEncoder () = new Concentus.Structs.OpusEncoder(OpusSampleRate, OpusChannels, OpusApplication.OPUS_APPLICATION_AUDIO)