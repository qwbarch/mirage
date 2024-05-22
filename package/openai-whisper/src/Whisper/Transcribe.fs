module Whisper.Transcribe

#nowarn "40"

open Whisper.API
open Mirage.Core.Async.Lock

type Transcriber =
    private
        {   whisper: Whisper
            lock: Lock
        }

let Transcriber whisper =
    {   whisper = whisper
        lock = createLock()
    }