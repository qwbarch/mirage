module Mirage.Core.Audio.Opus.Codec

#nowarn "44"

open OpusDotNet
open Concentus.Enums

let [<Literal>] OpusBitRate = 24_000
let [<Literal>] OpusSampleRate = 48_000
let [<Literal>] OpusChannels = 1
let [<Literal>] FrameSizeMs = 20
let [<Literal>] SamplesPerPacket = OpusSampleRate * FrameSizeMs / 1000
let [<Literal>] PacketPcmLength = SamplesPerPacket * 2

/// Decoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let OpusDecoder () = new OpusDecoder(FrameSizeMs, OpusSampleRate, OpusChannels)

/// Encoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let OpusEncoder () =
    let encoder = new Concentus.Structs.OpusEncoder(OpusSampleRate, OpusChannels, OpusApplication.OPUS_APPLICATION_AUDIO)
    encoder.UseVBR <- true
    encoder.Bitrate <- OpusBitRate
    encoder.UseConstrainedVBR <- true
    encoder.Complexity <- 10
    encoder.PredictionDisabled <- true
    encoder.UseDTX <- false
    encoder