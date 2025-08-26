module Mirage.Hook.Config

open FSharpPlus
open Unity.Netcode
open System.Reflection
open Newtonsoft.Json
open Mirage.Compatibility.Main
open Mirage.Domain.Config
open Mirage.Domain.Logger
open Mirage.Domain.Null

let syncConfig () =
    On.GameNetcodeStuff.PlayerControllerB.add_ConnectClientToPlayerObject(fun orig self ->
        if NetworkManager.Singleton.IsHost then
            registerHandler RequestSync
        else
            registerHandler ReceiveSync
            registerHandler FinishSync
            requestSync()
        orig.Invoke self
    )

    On.MenuManager.add_Start(fun orig self ->
        orig.Invoke self
        revertSync()
    )

    On.StartOfRound.add_Awake(fun orig self ->
        orig.Invoke self
        for prefab in GameNetworkManager.Instance.GetComponent<NetworkManager>().NetworkConfig.Prefabs.m_Prefabs do
            let enemyAI = prefab.Prefab.GetComponent<EnemyAI>()
            if isNotNull enemyAI then
                localConfig.RegisterEnemy enemyAI
        initEnemiesLethalConfig
            (Assembly.GetExecutingAssembly())
            (getEnemyConfigEntries())
    )

    On.Terminal.add_Start(fun orig self ->
        orig.Invoke self
        iter localConfig.RegisterStoreItem self.buyableItemsList
        if self.IsHost then
            initSyncedConfig()
            logInfo $"This configuration will be synced with clients: {JsonConvert.SerializeObject(getConfig(), Formatting.Indented)}"
    )

    On.StartOfRound.add_LoadShipGrabbableItems(fun orig self ->
        orig.Invoke self
        for item in self.allItemsList.itemsList do
            if item.isScrap then
                localConfig.RegisterScrapItem item
    )