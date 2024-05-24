namespace Mirage.Hook

#nowarn "40"

open System
open System.Collections.Generic
open StaticNetcodeLib
open Unity.Netcode
open FSharpx.Control
open Predictor.Lib
open Predictor.Domain
open Whisper.API
open Mirage.Domain.Logger
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.Lock
open Mirage.Unity.Temp

[<StaticNetcode>]
module VoiceRecognition =
    open System.Threading
    /// Min # of samples to wait for before transcribing.
    let [<Literal>] private MinSamples = 1024

    let private whisper, useCuda =
        logInfo "Loading WhisperS2T."
        Async.RunSynchronously startWhisper

    let threadId = Thread.CurrentThread.ManagedThreadId
    let syncContext = SynchronizationContext.Current
    let mutable private syncer = null

    let initTemp () =
        On.GameNetcodeStuff.PlayerControllerB.add_Awake(fun orig self ->
            orig.Invoke self
            ignore <| self.gameObject.AddComponent<TranscriptionSyncer>()
        )
        On.GameNetcodeStuff.PlayerControllerB.add_ConnectClientToPlayerObject(fun orig self ->
            orig.Invoke self
            syncer <- self.GetComponent<TranscriptionSyncer>()
        )

    let private transcribeChannel =
        let agent = new BlockingQueueAgent<SpeechDetection>(Int32.MaxValue)
        let sampleBuffer = new List<byte>()
        let lock = createLock()

        // TODO: Maybe make waiting for lock waitable from the caller?
        let processSamples waitForLock  =
            let run =
                async {
                    //logInfo "inside processSamples"
                    let samples = sampleBuffer.ToArray()
                    if samples.Length > 0 then
                        //logInfo $"processing samples: {samples.Length}"
                        let! transcriptions =
                            transcribe whisper
                                {   samplesBatch = [| samples |]
                                    language = "en"
                                }

                        // Send the transcribed text to the behaviour predictor.
                        logInfo $"userRegisterText (SpokeAtom): {transcriptions[0].text}"
                        userRegisterText <| SpokeAtom
                            {   text = transcriptions[0].text
                                start = DateTime.UtcNow
                            }
                        syncer.SendTranscription transcriptions[0].text
                        //logInfo "waiting 1 second as a test"
                        //do! Async.Sleep 1000
                        //logInfo "done transcribing"
                }
            Async.Start <| async {
                //logInfo "in processSamples Async.Start"
                if waitForLock then
                    do! withLock' lock run
                else if tryAcquire lock then
                    try do! run
                    finally lockRelease lock
            }

        //logInfo "consumer start (before voice recog)"
        let rec consumer =
            async {
                //logInfo "before consumer async get"
                let! speech = agent.AsyncGet()
                //logInfo "after consumer async get"
                match speech with
                    | SpeechStart ->
                        sampleBuffer.Clear()
                        logInfo "voice recognition start"
                    | SpeechEnd ->
                        logInfo "voice recognition end"
                        processSamples true
                        sampleBuffer.Clear()
                    | SpeechFound samples ->
                        //logInfo "voice recognition speech found"
                        sampleBuffer.AddRange <| toPCMBytes samples
                        //logInfo "before acquiring lock"
                        processSamples false
                do! consumer
            }
        Async.Start consumer
        agent

    /// Queue samples taken from the local player, to be transcribed whenever possible.
    let transcribeSpeech = transcribeChannel.AsyncAdd