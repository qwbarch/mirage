module Mirage.Hook.RecordAudio

open Dissonance.Audio.Capture

/// Allows us to hook into the live microphone feed to manually process.
type private MicrophoneSubscriber() =
    interface IMicrophoneSubscriber with
        member _.ReceiveMicrophoneData(buffer, format) = ()
        member _.Reset() = ()

let recordAudio() =
    let subscriber = MicrophoneSubscriber()

    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
    )