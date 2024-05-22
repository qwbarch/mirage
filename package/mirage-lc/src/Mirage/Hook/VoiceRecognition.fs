namespace Mirage.Hook

#nowarn "40"

open System
open System.Collections.Generic
open StaticNetcodeLib
open FSharpx.Control
open Unity.Netcode
open Mirage.Domain.Logger
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.Lock
open Whisper.API

//[<StaticNetcode>]
module VoiceRecognition =
    open Mirage.Core.Async.Fork
    open FSharpPlus
    /// Min # of samples to wait for before transcribing.
    let [<Literal>] private MinSamples = 1024

    let private whisper, useCuda =
        logInfo "Loading WhisperS2T."
        Async.RunSynchronously startWhisper

    let private channel =
        let agent = new BlockingQueueAgent<SpeechDetection>(Int32.MaxValue)
        let sampleBuffer = new List<byte>()
        let lock = createLock()

        // TODO: Maybe make waiting for lock waitable from the caller?
        let processSamples waitForLock  =
            let run =
                async {
                    logInfo "inside processSamples"
                    let samples = sampleBuffer.ToArray()
                    if samples.Length > 0 then
                        logInfo $"processing samples: {samples.Length}"
                        let! transcriptions =
                            transcribe whisper
                                {   samplesBatch = [| samples |]
                                    language = "en"
                                }
                        logInfo $"transcription: {transcriptions[0]}"
                        logInfo "waiting 1 second as a test"
                        do! Async.Sleep 1000
                        logInfo "done transcribing"
                }
            Async.Start <| async {
                logInfo "in processSamples Async.Start"
                if waitForLock then
                    do! withLock' lock run
                else
                    let x = tryAcquire lock
                    logInfo $"lock acquired: {x}"
                    if x then
                        try do! run
                        finally
                            logInfo "lock released"
                            lockRelease lock
            }

        logInfo "consumer start (before voice recog)"
        let rec consumer =
            async {
                logInfo "before consumer async get"
                let! speech = agent.AsyncGet()
                logInfo "after consumer async get"
                match speech with
                    | SpeechStart ->
                        sampleBuffer.Clear()
                        logInfo "voice recognition start"
                    | SpeechEnd ->
                        logInfo "voice recognition end"
                        processSamples true
                        sampleBuffer.Clear()
                    | SpeechFound samples ->
                        logInfo "voice recognition speech found"
                        sampleBuffer.AddRange <| toPCMBytes samples
                        logInfo "before acquiring lock"
                        processSamples false
                do! consumer
            }
        Async.Start consumer
        agent

    /// Queue samples taken from the local player, to be transcribed whenever possible.
    let transcribeSpeech = channel.AsyncAdd