module Mirage.Hook.RegisterPrefab

open FSharpPlus
open Unity.Netcode
open Mirage.Unity.AudioStream
open Mirage.Unity.MimicVoice
open Mirage.Unity.MimicPlayer
open Mirage.Unity.Predictor
open Mirage.Unity.Recognition

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
                        typeof<MimicPlayer>
                        typeof<MimicVoice>
                        typeof<Predictor>
                    ]
    )

    On.GameNetcodeStuff.PlayerControllerB.add_Awake(fun orig self ->
        orig.Invoke self
        iter (ignore << self.gameObject.AddComponent)
            [   typeof<Predictor>
                typeof<RemoteTranscriber>
            ]
    )