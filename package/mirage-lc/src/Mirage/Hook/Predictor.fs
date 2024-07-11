module Mirage.Hook.Predictor

open System.Collections
open Steamworks
open UnityEngine
open Unity.Netcode
open Predictor.Domain
open Predictor.Lib
open Mirage.Domain.Logger

let initPredictor () =
    On.Netcode.Transports.Facepunch.FacepunchTransport.add_Awake(fun _ self ->
        try
            try
                SteamClient.Init(self.steamAppId, false)
            finally
                ignore <| self.StartCoroutine(self.InitSteamworks())
        with | error ->
            if self.LogLevel <= LogLevel.Error then
                Debug.LogError $"[FacepunchTransport] - Caught an exeption during initialization of Steam client: {error}"
            startBehaviourPredictor <| Int 0uL
            logWarning "Steam is not initialized. MirageAI may not work as expected."
    )
    On.Netcode.Transports.Facepunch.FacepunchTransport.add_InitSteamworks(fun orig self ->
        seq {
            yield orig.Invoke self
            startBehaviourPredictor <| Int self.userSteamId
        } :?> IEnumerator
    )