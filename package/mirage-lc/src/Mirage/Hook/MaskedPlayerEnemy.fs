module Mirage.Hook.MaskedPlayerEnemy

open FSharpPlus
open UnityEngine
open Unity.Netcode
open System
open System.Collections.Generic
open Mirage.Compatibility.Main
open Mirage.Domain.Config
open Mirage.Domain.Logger
open Mirage.Domain.Null
open Mirage.Unity.MaskedAnimator
open Mirage.Unity.MimicPlayer

let [<Literal>] private MaskedEnemyName = "Masked"
let mutable private maskedPrefab = null

let hookMaskedEnemy maskedAnimationController =
    On.MaskedPlayerEnemy.add_Start(fun orig self ->
        try
            self.GetComponent<MimicPlayer>().StartMimicking()
        with | :? NullReferenceException as _ ->
            logWarning
                <| "\nFailed to initialize voice mimicking due to an incompatible mod."
                + $"\nEnemy name: {self.enemyType.enemyName}"
        orig.Invoke self
        if not <| getConfig().enableMaskTexture then
            self.GetComponentsInChildren<Transform>()
                |> filter _.name.StartsWith("HeadMask")
                |> iter _.gameObject.SetActive(false)
        if not <| getConfig().enableRadarSpin then
            let disable (transform: Transform) =
                transform.GetComponent<Animator>().enabled <- false
            self.GetComponentsInChildren<Transform>()
                |> tryFind _.name.StartsWith("MapDot")
                |> iter disable
        
        // Avoid replacing the runtime animator controller when not needed.
        if not (isLethalIntelligenceLoaded()) && getConfig().maskedItemSpawnChance > 0 then
            self.creatureAnimator.runtimeAnimatorController <- maskedAnimationController
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
            if isNotNull <| prefab.Prefab.GetComponent<MaskedPlayerEnemy>() then
                let maskedEnemy = prefab.Prefab.GetComponent<MaskedPlayerEnemy>()
                // Keep only the vanilla masked enemy.
                if maskedEnemy.enemyType.enemyName = MaskedEnemyName then
                    maskedPrefab <- maskedEnemy
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
                // Not all mods have the enemyType and/or enemyPrefab setup.
                isNotNull enemy
                    && isNotNull enemy.enemyType
                    && isNotNull enemy.enemyType.enemyPrefab
                    && enemy.enemyType.enemyName = maskedPrefab.enemyType.enemyName
            let logs = new List<string>()
            let minSpawnChance = float localConfig.MaskedSpawnChance.Value
            for level in StartOfRound.Instance.levels do
                ignore <| level.Enemies.RemoveAll isMaskedEnemy
                let mutable totalWeight =
                    level.Enemies
                        |> filter (not << isMaskedEnemy)
                        |> map _.rarity
                        |> sum
                if totalWeight > 0 then
                    let weight = int << ceil <| float totalWeight * minSpawnChance / (100.0 - minSpawnChance)
                    totalWeight <- totalWeight + weight
                    let spawnChance = float weight / float totalWeight * 100.0
                    logs.Add $"Level: {level.PlanetName}. Weight: {weight}. SpawnChance: {spawnChance:F2}%%"
                    let enemy = SpawnableEnemyWithRarity()
                    enemy.rarity <- weight
                    enemy.enemyType <- enemyType
                    enemy.enemyType.MaxCount <- localConfig.MaxMaskedSpawns.Value
                    level.Enemies.Add enemy
            logInfo <| "Adjusting spawn weights for masked enemies:\n" + String.Join("\n", logs)
    )

    On.MaskedPlayerEnemy.add_KillEnemy(fun orig self destroy ->
        orig.Invoke(self, destroy)
        self.GetComponent<MaskedAnimator>().OnDeath()
    )