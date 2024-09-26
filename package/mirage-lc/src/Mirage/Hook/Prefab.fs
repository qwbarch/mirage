module Mirage.Hook.Prefab

open FSharpPlus
open Unity.Netcode
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicPlayer
open Mirage.Unity.MirageVoice

let mutable initialized = false

let registerPrefab () =
    On.MenuManager.add_Start(fun orig self ->
        orig.Invoke self
        if not initialized then
            initialized <- true
            for prefab in GameNetworkManager.Instance.GetComponent<NetworkManager>().NetworkConfig.Prefabs.m_Prefabs do
                let enemyAI = prefab.Prefab.GetComponent<EnemyAI>()
                if not <| isNull enemyAI then
                    iter (ignore << enemyAI.gameObject.AddComponent)
                        [   typeof<AudioStream>
                            typeof<MimicPlayer>
                            typeof<MimicVoice>
                        ]
    )