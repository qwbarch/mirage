namespace Mirage.Hook

#nowarn "40"

open System
open System.Collections.Generic
open System.Threading
open FSharpPlus
open FSharpx.Control
open Predictor.Lib
open Predictor.Domain
open Whisper.API
open Mirage.Domain.Logger
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.PCM
open Mirage.Core.Async.Lock
open Mirage.Unity.Temp

module VoiceRecognition =
    open Mirage.Core.Audio.File.Mp3Reader
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
        let agent = new BlockingQueueAgent<Tuple<SpeechDetection, Guid>>(Int32.MaxValue)
        let sampleBuffer = new List<byte>()
        let lock = createLock()

        // TODO: Maybe make waiting for lock waitable from the caller?
        let processSamples finalSample fileId =
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
                        let spokeAtom =
                            {   text = transcriptions[0].text
                                start = DateTime.UtcNow
                            }
                        userRegisterText <| SpokeAtom spokeAtom
                        syncer.SendTranscription transcriptions[0].text

                        if finalSample then
                            use! file = readMp3File "{Application.dataPath}/../Mirage/{fileId}.mp3"
                            logInfo $"SpokeRecordingAtom. Total milliseconds: {file.reader.TotalTime.TotalMilliseconds}"
                            userRegisterText <| SpokeRecordingAtom
                                {   spokeAtom = spokeAtom
                                    whisperTimings = []
                                    vadTimings = []
                                    audioInfo =
                                        {   fileId = fileId
                                            duration = int file.reader.TotalTime.TotalMilliseconds
                                        }
                                }
                        //logInfo "waiting 1 second as a test"
                        //do! Async.Sleep 1000
                        //logInfo "done transcribing"
                }
            Async.Start <| async {
                //logInfo "in processSamples Async.Start"
                if finalSample then
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
                    | (SpeechStart, _) ->
                        sampleBuffer.Clear()
                        logInfo "voice recognition start"
                    | (SpeechEnd, fileId) ->
                        logInfo "voice recognition end"
                        processSamples true fileId
                        sampleBuffer.Clear()
                    | (SpeechFound samples, fileId) ->
                        //logInfo "voice recognition speech found"
                        sampleBuffer.AddRange <| toPCMBytes samples
                        //logInfo "before acquiring lock"
                        processSamples false fileId
                do! consumer
            }
        Async.Start consumer
        agent

    /// Queue samples taken from the local player, to be transcribed whenever possible.
    let transcribeSpeech = curry transcribeChannel.AsyncAdd