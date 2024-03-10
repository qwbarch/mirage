module Mirage.Utilities.Audio.VAD

/// Detect if speech is found, using an arbitrary VAD algorithm.
type VadDetector =
    private
        {   /// A function that detects if speech is found.<br />
            /// Assumes the given pcm data is 16khz, and contains 30ms of audio.
            detectSpeech: float32[] -> bool
            /// A function that gets called when speech detection starts.
            onSpeechStart: unit -> unit
            /// A function that gets called every frame where speech is detected.
            onSpeechDetected: float32[] -> unit
            /// A function that gets called when speech detection is over.
            onSpeechEnd: unit -> unit
            silentSamples: float32
            speechPadSamples: float32
        }