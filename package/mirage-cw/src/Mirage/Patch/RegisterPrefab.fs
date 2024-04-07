module Mirage.Patch.RegisterPrefab

open System
open UnityEngine
open FSharpPlus
open HarmonyLib
open Photon.Pun
open Photon.Voice.PUN
open Photon.Voice.Unity
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicVoice

type RegisterPrefab() =
    static let mutable registered = false

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<Player>, "Start")>]
    static member ``save playback prefab``(__instance: Player) =
        if isNull PlaybackPrefab then
            PlaybackPrefab <- Object.Instantiate<GameObject> <| Player.localPlayer.transform.Find("HeadPosition/Voice").gameObject
            Object.DontDestroyOnLoad PlaybackPrefab
            PlaybackPrefab.SetActive false
            let removeComponent : Type -> unit = Object.Destroy << PlaybackPrefab.GetComponent
            iter removeComponent
                [   typeof<PhotonVoiceView>
                    typeof<PhotonView>
                    typeof<Speaker>
                    typeof<Recorder>
                    typeof<PlayerVoiceHandler>
                ]

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<RoundSpawner>, "Start")>]
    static member ``register prefabs for enemies``(__instance: RoundSpawner) =
        if not registered then
            registered <- true
            for prefab in __instance.possibleSpawns do
                let group = prefab.GetComponent<MonsterGroupClose>()
                if isNull group then
                    iter (ignore << prefab.AddComponent)
                        [   typeof<AudioStream>
                            typeof<MimicVoice>
                        ]