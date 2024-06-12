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
open Mirage.Core.Audio.File.Mp3Writer
open Mirage.Core.Async.LVar
open Mirage.Unity.Temp
open Mirage.Unity.MimicVoice

module VoiceRecognition =
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

    let private toVADTiming vadFrame =
        let atom =
            {   speakerId = Guid guid
                prob = float vadFrame.probability
            }
        (vadFrame.elapsedTime, atom)

    let private transcribeChannel =
        let agent = new BlockingQueueAgent<Tuple<SpeechDetection, Mp3Writer>>(Int32.MaxValue)
        let sampleBuffer = new List<byte>()
        let lock = createLock()
        let mutable sentenceId = Guid.NewGuid()

        // TODO: Maybe make waiting for lock waitable from the caller?
        let processSamples mp3Writer (currentVADFrame: option<VADFrame>) (vadFrames: option<list<VADFrame> * int>) (samples: byte[]) =
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
                        match (currentVADFrame, vadFrames) with
                            // Speech is still detected. Send the current transcription.
                            | Some vadFrame, None ->
                                let spokeAtom =
                                    {   text = transcriptions[0].text
                                        sentenceId = sentenceId
                                        elapsedMillis = vadFrame.elapsedTime
                                        transcriptionProb = float transcriptions[0].avgLogProb
                                        nospeechProb = float transcriptions[0].noSpeechProb
                                    }
                                userRegisterText <| SpokeAtom spokeAtom
                                Async.StartImmediate <| async {
                                    let! mimics = readLVar mimicsVar
                                    flip iter mimics <| fun (mimickingPlayer, mimicId) ->
                                        if StartOfRound.Instance.localPlayerController = mimickingPlayer then
                                            logInfo $"mimicRegisterText SpokeAtom: {mimicId}."
                                            mimicRegisterText mimicId <| SpokeAtom spokeAtom
                                }
                                syncer.SendTranscription(
                                    sentenceId,
                                    transcriptions[0].text,
                                    guid,
                                    vadFrame.elapsedTime,
                                    transcriptions[0].avgLogProb,
                                    transcriptions[0].noSpeechProb
                                )
                            // Speech is over. Send the final transcription with all VAD timings.
                            | None, Some (vadTimings, audioDuration) ->
                                logInfo "final sample, sending spokerecordingatom"
                                let filePath = getFilePath mp3Writer
                                logInfo $"path: {filePath}"
                                logInfo $"SpokeRecordingAtom. Total milliseconds: {audioDuration}"
                                logInfo $"VAD timings: {vadTimings}"
                                let spokeRecordingAtom =
                                    SpokeRecordingAtom
                                        {   spokeAtom =
                                                {   text = transcriptions[0].text
                                                    sentenceId = sentenceId
                                                    elapsedMillis = audioDuration
                                                    transcriptionProb = float transcriptions[0].avgLogProb
                                                    nospeechProb = float transcriptions[0].noSpeechProb
                                                }
                                            whisperTimings = []
                                            vadTimings = toVADTiming <!> vadTimings
                                            audioInfo =
                                                {   fileId = getFileId mp3Writer
                                                    duration = audioDuration
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
                                sentenceId <- Guid.NewGuid()
                            | _, _ -> logError "Invalid state while running voice recognition."
                }
            Async.StartImmediate <| async {
                if Option.isSome vadFrames then
                    logInfo "voice recognition end"
                    logInfo "final sample. acquiring lock"
                    do! withLock' lock run
                    logInfo "final sample. finished"
                else if tryAcquire lock then
                    try do! run
                    finally lockRelease lock
            }

        let rec consumer =
            async {
                let! speech = agent.AsyncGet()
                match speech with
                    | SpeechStart, _ ->
                        logInfo "voice recognition start"
                        sampleBuffer.Clear()
                        syncer.VoiceActivityStart guid
                    | SpeechEnd (vadTimings, _, audioDuration), mp3Writer ->
                        processSamples mp3Writer None (Some (vadTimings, audioDuration)) << Array.copy <| sampleBuffer.ToArray()
                        sampleBuffer.Clear()
                    | SpeechFound (vadFrame, samples), mp3Writer ->
                        sampleBuffer.AddRange <| toPCMBytes samples
                        processSamples mp3Writer (Some vadFrame) None << Array.copy <| sampleBuffer.ToArray()
                do! consumer
            }
        Async.Start consumer
        agent

    /// Queue samples taken from the local player, to be transcribed whenever possible.
    let transcribeSpeech = curry transcribeChannel.AsyncAdd