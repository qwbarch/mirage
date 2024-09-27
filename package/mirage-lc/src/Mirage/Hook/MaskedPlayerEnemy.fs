module Mirage.Hook.MaskedPlayerEnemy

open FSharpPlus
open UnityEngine
open System
open System.Collections.Generic
open Mirage.Domain.Config
open Mirage.Unity.MimicPlayer
open Unity.Netcode
open Mirage.Domain.Logger

let mutable private maskedPrefab = null

let hookMaskedEnemy () =
    On.MaskedPlayerEnemy.add_Start(fun orig self ->
        self.GetComponent<MimicPlayer>().StartMimicking()
        orig.Invoke self
        if not <| getConfig().enableMaskTexture then
            self.GetComponentsInChildren<Transform>()
                |> filter _.name.StartsWith("HeadMask")
                |> iter _.gameObject.SetActive(false)
    )

    On.MaskedPlayerEnemy.add_SetHandsOutClientRpc(fun orig self _ ->
        orig.Invoke(self, getConfig().enableArmsOut)
    )

    On.MaskedPlayerEnemy.add_SetHandsOutServerRpc(fun orig self _ ->
        orig.Invoke(self, getConfig().enableArmsOut)
    )

    On.StartOfRound.add_EndOfGame(fun orig self bodiesInsured connectedPlayersOnServer scrapCollected ->
        // After a round is over, the player's dead body is still set.
        // This causes the teleporter to attempt to move the dead body, which always fails.
        for player in self.allPlayerScripts do
            player.deadBody <- null
        orig.Invoke(self, bodiesInsured, connectedPlayersOnServer, scrapCollected)
    )

    On.GameNetworkManager.add_Start(fun orig self ->
        orig.Invoke self
        for prefab in NetworkManager.Singleton.NetworkConfig.Prefabs.m_Prefabs do
            if not (isNull <| prefab.Prefab.GetComponent<MaskedPlayerEnemy>()) && isNull maskedPrefab then
                maskedPrefab <- prefab.Prefab.GetComponent<MaskedPlayerEnemy>()
    )

    On.TimeOfDay.add_Start(fun orig self ->
        orig.Invoke self
        if self.IsHost && localConfig.EnableSpawnControl.Value then
            let enemyType =
                let enemy = Object.Instantiate<EnemyType> maskedPrefab.enemyType
                let spawnCurve = enemy.probabilityCurve
                let addKey time value = ignore << spawnCurve.AddKey <| Keyframe(time, value)
                spawnCurve.ClearKeys()
                addKey 0f 0f
                addKey 0.19f 0f
                addKey 0.2f 0.5f
                addKey 0.5f 10f
                addKey 0.9f 15f
                addKey 1f 1f
                enemy
            let isMaskedEnemy (enemy: SpawnableEnemyWithRarity) =
                not << isNull <| enemy.enemyType.enemyPrefab.GetComponent<MaskedPlayerEnemy>()
            let logs = new List<string>()
            let minSpawnChance = localConfig.MaskedSpawnChance.Value
            for level in StartOfRound.Instance.levels do
                ignore <| level.Enemies.RemoveAll isMaskedEnemy
                let mutable totalWeight =
                    level.Enemies
                        |> map _.rarity
                        |> sum
                if totalWeight <> 0 then
                    let weight = int << ceil <| float totalWeight * minSpawnChance / (100.0 - minSpawnChance)
                    totalWeight <- totalWeight + weight
                    let spawnChance = float weight / float totalWeight * 100.0
                    logs.Add $"Level: {level.PlanetName}. Weight: {weight}. SpawnChance: {spawnChance:F2}%%"
                    let enemy = new SpawnableEnemyWithRarity()
                    enemy.rarity <- weight
                    enemy.enemyType <- enemyType
                    enemy.enemyType.MaxCount <- localConfig.MaxMaskedSpawns.Value
                    level.Enemies.Add enemy
            logInfo <| "Adjusting spawn weights for masked enemies:\n" + String.Join("\n", logs)
    )