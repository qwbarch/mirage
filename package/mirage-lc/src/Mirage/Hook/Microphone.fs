namespace Mirage.Hook

open NAudio.Wave
open StaticNetcodeLib
open Unity.Netcode
open Mirage.Domain.Audio.Microphone
open Mirage.Core.Audio.Microphone.Resampler
open Mirage.Core.Async.LVar

[<StaticNetcode>]
module Microphone =
    let mutable private cudaAvailable = false
    let mutable private transcribeViaHost = None

    [<AllowNullLiteral>]
    type MicrophoneSubscriber(param) as self =
        do MicrophoneSubscriber.Instance <- self

        static member val Instance = null with get, set
        member val MicrophoneProcessor = MicrophoneProcessor param

        interface Dissonance.Audio.Capture.IMicrophoneSubscriber with
            member this.ReceiveMicrophoneData(buffer, format) =
                Async.StartImmediate <|
                    processMicrophone this.MicrophoneProcessor
                        {   samples = buffer.ToArray()
                            format = WaveFormat(format.SampleRate, format.Channels)
                        }
            member _.Reset() = ()

    [<ClientRpc>]
    let private transcribeViaHostClientRpc (_: ClientRpcParams) useHost =
        if not <| StartOfRound.Instance.IsHost then
            Async.StartImmediate <| writeLVar_ transcribeViaHost.Value useHost //(useHost && not cudaAvailable)

    let readMicrophone param =
        cudaAvailable <- param.cudaAvailable
        transcribeViaHost <- Some param.transcribeViaHost

        On.Dissonance.DissonanceComms.add_Start(fun orig self ->
            orig.Invoke self
            self.SubscribeToRecordedAudio <| MicrophoneSubscriber param
        )

        // Normally during the opening doors sequence, the game suffers from dropped audio frames, causing recordings to sound glitchy.
        // To reduce the likelihood of recording glitched sounds, audio recordings only start after the sequence is completely finished.
        On.StartOfRound.add_openingDoorsSequence(fun orig self ->
            Async.StartImmediate <| writeLVar_ param.isReady true
            orig.Invoke self
        )

        On.StartOfRound.add_OnDestroy(fun orig self ->
            orig.Invoke self
            Async.StartImmediate <| async {
                do! writeLVar_ param.isReady false
                do! writeLVar_ param.transcribeViaHost false
            }
        )

        // This hook only runs on the host.
        On.StartOfRound.add_OnClientConnect(fun orig self clientId ->
            orig.Invoke(self, clientId)
            let mutable rpcParams = ClientRpcParams()
            rpcParams.Send <- ClientRpcSendParams()
            rpcParams.Send.TargetClientIds <- [|clientId|]
            transcribeViaHostClientRpc rpcParams param.cudaAvailable
        )