module Mirage.Hook.RegisterPrefab

open FSharpPlus
open Unity.Netcode
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicVoice

let mutable internal maskedPrefab: MaskedPlayerEnemy = null

let registerPrefab () =
    On.GameNetworkManager.add_Start(fun orig self ->
        orig.Invoke self
        let networkManager = self.GetComponent<NetworkManager>()
        for prefab in networkManager.NetworkConfig.Prefabs.Prefabs do
            let enemyAI = prefab.Prefab.GetComponent<EnemyAI>()
            // TODO: Make this not hardcoded to only work for MaskedPlayerEnemy.
            if not <| isNull enemyAI && enemyAI :? MaskedPlayerEnemy then
                iter (ignore << enemyAI.gameObject.AddComponent)
                    [   typeof<AudioStream>
                        typeof<MimicVoice>
                    ]
                if enemyAI :? MaskedPlayerEnemy then
                    maskedPrefab <- enemyAI :?> MaskedPlayerEnemy
    )