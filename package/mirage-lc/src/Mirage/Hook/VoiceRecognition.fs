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

//[<StaticNetcode>]
module VoiceRecognition =
    // Temporarily hard-coding the local user's id.
    let guid = new Guid("37f6b68d-3ce2-4cde-9dc9-b6a68ccf002c")

    /// Min # of samples to wait for before transcribing.
    let [<Literal>] private MinSamples = 1024

    let private whisper, useCuda =
        logInfo "Loading WhisperS2T."
        Async.RunSynchronously startWhisper

    let private isHost () = StartOfRound.Instance.IsHost

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
                        //userRegisterText <| SpokeAtom
                        //    {   text = transcriptions[0].text
                        //        start = DateTime.UtcNow
                        //    }
                        //logInfo $"transcription: {transcriptions[0]}"
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

    let private sendTranscription (userId: string) text =
        let guid = Guid <| new Guid(userId)
        logInfo $"HeardAtom. Speaker: {guid}. Text: {text}"
        //userRegisterText <| HeardAtom
        //    {   text = text
        //        start = DateTime.UtcNow
        //        speakerClass = guid
        //        speakerId = guid
        //    }

    //[<ClientRpc>]
    //let private sendTranscriptionClientRpc () =
    //    logInfo "client rpc invoked"

    //[<ServerRpc>]
    //let private sendTranscriptionServerRpc () =
    //    logInfo "server rpc invoked"

    //[<ClientRpc>]
    //let private sendTranscriptionClientRpc userId text =
    //    if not <| isHost() && guid.ToString() <> userId then
    //        sendTranscription userId text
    
    //[<ServerRpc>]
    //let private sendTranscriptionServerRpc (userId: string) text =
    //    if isHost() then
    //        sendTranscription userId text
    //        sendTranscriptionClientRpc userId text

    //let private rpcChannel =
    //    let agent = new BlockingQueueAgent<Transcription>(Int32.MaxValue)
    //    let rec consumer =
    //        async {
    //            let! transcription = agent.AsyncGet()
    //            let rpc =
    //                if isHost() then
    //                    sendTranscriptionClientRpc
    //                else
    //                    sendTranscriptionServerRpc
    //            //rpc (guid.ToString()) transcription.text
    //            rpc()
    //            do! consumer
    //        }
    //    Async.StartImmediate consumer
    //    agent