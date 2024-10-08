module Mirage.Hook.Config

open Unity.Netcode
open Mirage.Domain.Config

let syncConfig () =
    On.GameNetcodeStuff.PlayerControllerB.add_ConnectClientToPlayerObject(fun orig self ->
        orig.Invoke self
        if NetworkManager.Singleton.IsHost then
            registerHandler RequestSync
        else
            registerHandler ReceiveSync
            requestSync()
    )

    On.GameNetworkManager.add_StartDisconnect(fun orig self ->
        orig.Invoke self
        revertSync()
    )

    On.StartOfRound.add_Awake(fun orig self ->
        orig.Invoke self
        for prefab in GameNetworkManager.Instance.GetComponent<NetworkManager>().NetworkConfig.Prefabs.m_Prefabs do
            let enemyAI = prefab.Prefab.GetComponent<EnemyAI>()
            if not <| isNull enemyAI then
                localConfig.RegisterEnemy enemyAI
        localConfig.ClearOrphanedEntries()
    )