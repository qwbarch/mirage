module Mirage.Patch.RegisterPrefab

open System
open System.Collections.Generic
open FSharpPlus
open HarmonyLib
open Unity.Netcode
open GameNetcodeStuff
open Mirage.Core.Logger
open Mirage.Core.Config
open Mirage.Core.Field
open Mirage.Unity.MimicVoice
open Mirage.Unity.AudioStream
open Mirage.Unity.Network
open Mirage.Unity.MimicPlayer
open Mirage.Unity.PlayerReanimator
open UnityEngine

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
                let enemyType =
                    if config.useCustomSpawnCurve then
                        let enemy = Object.Instantiate prefab.enemyType :?> EnemyType
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
                    else
                        prefab.enemyType

                let minSpawnChance = float config.overrideSpawnChance
                let isMaskedEnemy (enemy: SpawnableEnemyWithRarity) =
                    not << isNull <| enemy.enemyType.enemyPrefab.GetComponent<MaskedPlayerEnemy>()
                let logs = new List<string>()
                for level in playerManager.levels do
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
                        level.Enemies.Add enemy
                logInfo <| "Adjusting spawn weights for masked enemies:\n" + String.Join("\n", logs)
        }

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<PlayerControllerB>, "Awake")>]
    static member ``register player reanimator prefab``(__instance: PlayerControllerB) =
        ignore <| __instance.gameObject.AddComponent<PlayerReanimator>()