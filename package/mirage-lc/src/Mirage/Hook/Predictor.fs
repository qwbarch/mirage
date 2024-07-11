module Mirage.Hook.Predictor

open System
open System.Collections
open Steamworks
open UnityEngine
open Unity.Netcode
open Predictor.Domain
open Predictor.Lib
open Mirage.Domain.Logger
open Mirage.Domain.Audio.Recording
open FSharpPlus
open System.IO

let initPredictor predictorDirectory =
    let initModel (steamId: EntityId) =
        Async.RunSynchronously <| async {
            logInfo "Loading recordings"
            let toGuid (x: string) = new Guid(x)
            let! recordings =
                getRecordings
                    |> map (map (Path.GetFileNameWithoutExtension >> toGuid) >> List.ofArray)
            logInfo $"recordings: {recordings}"
            do! initBehaviourPredictor
                    logInfo
                    logWarning
                    logError
                    steamId
                    predictorDirectory
                    recordings
                    Int32.MaxValue // Storage limit.
                    Int32.MaxValue // Memory limit.
        }
    On.Netcode.Transports.Facepunch.FacepunchTransport.add_Awake(fun _ self ->
        try
            try
                SteamClient.Init(self.steamAppId, false)
            finally
                ignore <| self.StartCoroutine(self.InitSteamworks())
        with | error ->
            if self.LogLevel <= LogLevel.Error then
                Debug.LogError $"[FacepunchTransport] - Caught an exeption during initialization of Steam client: {error}"
            initModel <| Int 0uL
            logWarning "Steam is not initialized. MirageAI may not work as expected."
    )
    On.Netcode.Transports.Facepunch.FacepunchTransport.add_InitSteamworks(fun orig self ->
        seq {
            yield orig.Invoke self
            initModel <| Int self.userSteamId
        } :?> IEnumerator
    )