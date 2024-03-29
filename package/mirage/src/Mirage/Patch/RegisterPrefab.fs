module Mirage.Patch.RegisterPrefab

open FSharpPlus
open HarmonyLib
open Unity.Netcode
open System.Collections.Generic
open Mirage.Core.Logger
open Mirage.Core.Config
open Mirage.Core.Field
open Mirage.Unity.MimicVoice
open Mirage.Unity.AudioStream
open Mirage.Unity.Network
open Mirage.Unity.MimicPlayer
open System

let private initPrefabs<'A when 'A : null and 'A :> EnemyAI> (networkPrefab: NetworkPrefab) =
    let enemyAI = networkPrefab.Prefab.GetComponent<'A>() :> EnemyAI
    if not <| isNull enemyAI then
        iter (ignore << enemyAI.gameObject.AddComponent)
            [   typeof<AudioStream>
                typeof<MimicPlayer>
                typeof<MimicVoice>
            ]

let private get<'A> : Getter<'A> = getter "RegisterPrefab"

type RegisterPrefab() =
    static let Prefab = field()
    static let getPrefab = get Prefab "Prefab"

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<GameNetworkManager>, "Start")>]
    static member ``register network prefab``(__instance: GameNetworkManager) =
        handleResult <| monad' {
            let networkManager = __instance.GetComponent<NetworkManager>()
            flip iter networkManager.NetworkConfig.Prefabs.m_Prefabs <| fun prefab ->
                if isPrefab<EnemyAI> prefab then
                    initPrefabs prefab
            let! prefab =
                findNetworkPrefab<MaskedPlayerEnemy> networkManager
                    |> Option.toResultWith "MaskedPlayerEnemy network prefab is missing. This is likely due to a mod incompatibility"
            prefab.enemyType.MaxCount <- 2
            set Prefab prefab
        }

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<TimeOfDay>, "Start")>]
    static member ``modify natural spawns for masked enemies``() =
        handleResult <| monad' {
            let playerManager = StartOfRound.Instance
            let config = getConfig()
            if playerManager.IsHost && config.enableOverrideSpawnChance then
                let! prefab = getPrefab "``modify natural spawns for masked enemies``"
                let minSpawnChance = config.overrideSpawnChance
                let isMaskedEnemy (enemy: SpawnableEnemyWithRarity) =
                    not << isNull <| enemy.enemyType.enemyPrefab.GetComponent<MaskedPlayerEnemy>()
                let logs = new List<string>()
                for level in playerManager.levels do
                    ignore <| level.Enemies.RemoveAll isMaskedEnemy
                    let mutable totalWeight =
                        level.Enemies
                            |> map _.rarity
                            |> fold (+) 0
                    if totalWeight <> 0 then
                        let weight = int << ceil <| float totalWeight * minSpawnChance / (100.0 - minSpawnChance)
                        totalWeight <- totalWeight + weight
                        let spawnChance = float weight / float totalWeight * 100.0
                        logs.Add $"Level: {level.PlanetName}. Weight: {weight}. SpawnChance: {spawnChance:F2}%%"
                        let enemy = new SpawnableEnemyWithRarity()
                        enemy.rarity <- weight
                        enemy.enemyType <- prefab.enemyType
                        level.Enemies.Add enemy
                logInfo <| "Adjusting spawn weights for masked enemies:\n" + String.Join("\n", logs)
        }