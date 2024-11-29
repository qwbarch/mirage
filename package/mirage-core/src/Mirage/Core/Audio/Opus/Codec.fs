module Mirage.Core.Audio.Opus.Codec

open Concentus
open Concentus.Enums

let [<Literal>] OpusSampleRate = 48_000
let [<Literal>] OpusChannels = 1

/// Decoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let OpusDecoder () = OpusCodecFactory.CreateDecoder(OpusSampleRate, OpusChannels)

/// Encoder with 48_000 sample rate and 1 channel, since ogg opus files only supports that.
let OpusEncoder () = OpusCodecFactory.CreateEncoder(OpusSampleRate, OpusChannels, OpusApplication.OPUS_APPLICATION_AUDIO)