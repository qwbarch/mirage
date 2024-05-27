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
open Mirage.Core.Audio.File.Mp3Reader
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Unity.Temp

module VoiceRecognition =
    open Mirage.Core.Async.LVar
    open Mirage.Unity.MimicVoice
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
        let agent = new BlockingQueueAgent<Tuple<SpeechDetection, Mp3Writer>>(Int32.MaxValue)
        let sampleBuffer = new List<byte>()
        let lock = createLock()

        // TODO: Maybe make waiting for lock waitable from the caller?
        let processSamples mp3Writer finalSample (samples: byte[]) =
            let run =
                async {
                    //logInfo "inside processSamples"
                    if samples.Length > 0 then
                        //logInfo $"processing samples: {samples.Length}"
                        let! transcriptions =
                            transcribe whisper
                                {   samplesBatch = [| samples |]
                                    language = "en"
                                }

                        let spokeAtom =
                            {   text = transcriptions[0].text
                                start = getCreationTime mp3Writer
                            }
                        if not finalSample then
                            userRegisterText <| SpokeAtom spokeAtom
                            Async.StartImmediate <| async {
                                let! mimics = readLVar mimicsVar
                                flip iter mimics <| fun (mimickingPlayer, mimicId) ->
                                    if StartOfRound.Instance.localPlayerController = mimickingPlayer then
                                        logInfo $"mimicRegisterText SpokeAtom: {mimicId}."
                                        mimicRegisterText mimicId <| SpokeAtom spokeAtom
                            }
                            syncer.SendTranscription(getCreationTime mp3Writer, transcriptions[0].text)
                        else
                            logInfo "final sample, sending spokerecordingatom"
                            let filePath = getFilePath mp3Writer
                            logInfo $"path: {filePath}"
                            use! file = readMp3File filePath
                            logInfo $"SpokeRecordingAtom. Total milliseconds: {file.reader.TotalTime.TotalMilliseconds}"
                            let spokeRecordingAtom =
                                SpokeRecordingAtom
                                    {   spokeAtom = spokeAtom
                                        whisperTimings = []
                                        vadTimings = []
                                        audioInfo =
                                            {   fileId = getFileId mp3Writer
                                                duration = int file.reader.TotalTime.TotalMilliseconds
                                            }
                                    }
                            userRegisterText spokeRecordingAtom
                            Async.StartImmediate <| async {
                                let! mimics = readLVar mimicsVar
                                flip iter mimics <| fun (mimickingPlayer, mimicId) ->
                                    if StartOfRound.Instance.localPlayerController = mimickingPlayer then
                                        logInfo $"mimicRegisterText SpokeRecordingAtom: {mimicId}."
                                        mimicRegisterText mimicId spokeRecordingAtom
                            }
                        //logInfo "waiting 1 second as a test"
                        //do! Async.Sleep 1000
                        //logInfo "done transcribing"
                }
            Async.StartImmediate <| async {
                if finalSample then
                    logInfo "voice recognition end"
                    logInfo "final sample. acquiring lock"
                    do! withLock' lock run
                    logInfo "final sample. finished"
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
                    | (SpeechStart, mp3Writer) ->
                        logInfo "voice recognition start"
                        sampleBuffer.Clear()
                        syncer.VoiceActivityStart(guid, getCreationTime mp3Writer)
                    | (SpeechEnd, mp3Writer) ->
                        processSamples mp3Writer true << Array.copy <| sampleBuffer.ToArray()
                        sampleBuffer.Clear()
                    | (SpeechFound samples, mp3Writer) ->
                        //logInfo "voice recognition speech found"
                        sampleBuffer.AddRange <| toPCMBytes samples
                        //logInfo "before acquiring lock"
                        processSamples mp3Writer false << Array.copy <| sampleBuffer.ToArray()
                do! consumer
            }
        Async.Start consumer
        agent

    /// Queue samples taken from the local player, to be transcribed whenever possible.
    let transcribeSpeech = curry transcribeChannel.AsyncAdd