module Mirage.Domain.Audio.Sender

open Mirage.Core.Audio.Opus.Reader
open UnityEngine.Playables

/// Responsible for sending opus audio packets, to be received by a __AudioReceiver__.
type AudioSender =
    {   opusReader: OpusReader
    }

type AudioSenderArgs =
    {   sendFrame: FrameData -> unit
        opusReader: OpusReader
    }

let AudioSender args = ()