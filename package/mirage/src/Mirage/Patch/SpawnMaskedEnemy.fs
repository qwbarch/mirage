module Mirage.Patch.SpawnMaskedEnemy

open FSharpPlus
open HarmonyLib
open GameNetcodeStuff
open System.Collections.Generic
open Unity.Netcode
open UnityEngine
open UnityEngine.AI
open Mirage.Core.Config
open Mirage.Core.Field
open Mirage.Core.Logger
open Mirage.Unity.Network
open Mirage.Unity.MimicPlayer

let private get<'A> = getter<'A> "SpawnMaskedEnemy"

type SpawnMaskedEnemy() =
    static let random = new System.Random()
    static let killedPlayers = new HashSet<uint64>()

    static let MaskItem = field<HauntedMaskItem>()
    static let getMaskItem = get MaskItem "MaskItem"

    static let spawnMaskedEnemy (player: PlayerControllerB) =
        handleResult <| monad' {
            let methodName = "spawnMaskedEnemy"
            let! maskItem = getMaskItem methodName
            let rotationY = player.transform.eulerAngles.y
            let maskedEnemy =
                Object.Instantiate<GameObject>(
                    maskItem.mimicEnemy.enemyPrefab,
                    player.transform.position,
                    Quaternion.Euler <| Vector3(0f, rotationY, 0f)
                )
            maskedEnemy.GetComponent<MaskedPlayerEnemy>().mimickingPlayer <- player
            maskedEnemy.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene = true)
            maskedEnemy.GetComponent<MimicPlayer>().DeactivateBody()
        }

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<GameNetworkManager>, "Start")>]
    static member ``save mask prefab for later use``(__instance: GameNetworkManager) =
        handleResult <| monad' {
            let! maskItem =
                findNetworkPrefab<HauntedMaskItem> (__instance.GetComponent<NetworkManager>())
                    |> Option.toResultWith "HauntedMaskItem network prefab is missing. This is likely due to a mod incompatibility."
            set MaskItem maskItem
        }

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<StartOfRound>, "StartGame")>]
    static member ``reset killed players``() = killedPlayers.Clear()

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<PlayerControllerB>, "KillPlayerServerRpc")>]
    static member ``spawn a masked enemy on player death (if configuration is enabled)``(
        __instance: PlayerControllerB,
        causeOfDeath: int,
        deathAnimation: int,
        spawnBody: bool,
        bodyVelocity: Vector3
    ) =
        if __instance.IsHost && killedPlayers.Add __instance.playerClientId then
            let playerPosition = __instance.transform.position
            let isOnNavMesh =
                let mutable meshHit = new NavMeshHit()
                NavMesh.SamplePosition(playerPosition, &meshHit, 1f, NavMesh.AllAreas)
                    && Mathf.Approximately(playerPosition.x, meshHit.position.x)
                    && Mathf.Approximately(playerPosition.z, meshHit.position.z)
            let playerKilledByMaskItem = 
                causeOfDeath = int CauseOfDeath.Suffocation
                    && spawnBody
                    && bodyVelocity.Equals Vector3.zero
            let playerKilledByMaskedEnemy =
                causeOfDeath = int CauseOfDeath.Strangulation
                    && deathAnimation = 4
            let config = getConfig()
            let isPlayerAloneRequired = not config.spawnOnlyWhenPlayerAlone || __instance.isPlayerAlone
            let spawnRateSuccess () = random.Next(1, 101) <= config.spawnOnPlayerDeath

            // isOnNavMesh is false while on the ship.
            if (isOnNavMesh || __instance.isInHangarShipRoom && StartOfRound.Instance.shipHasLanded)
                && not playerKilledByMaskItem
                && not playerKilledByMaskedEnemy
                && spawnBody
                && isPlayerAloneRequired
                && spawnRateSuccess()
            then
                spawnMaskedEnemy __instance

    [<HarmonyPrefix>]
    [<HarmonyPatch(typeof<MaskedPlayerEnemy>)>]
    [<HarmonyPatch("SetHandsOutServerRpc")>]
    [<HarmonyPatch("SetHandsOutClientRpc")>]
    static member ``disable mirage hands out``(setOut: byref<bool>) = setOut <- getConfig().enableArmsOut

    [<HarmonyPrefix>]
    [<HarmonyPatch(typeof<MaskedPlayerEnemy>, "Start")>]
    static member ``start mimicking player``(__instance: MaskedPlayerEnemy) =
        handleResult <| monad' {
            let! mimicPlayer =
                Option.ofObj (__instance.GetComponent<MimicPlayer>())
                    |> Option.toResultWith "MimicPlayer component could not be found, likely due to a mod incompatibility."
            mimicPlayer.StartMimicking()
        }

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<MaskedPlayerEnemy>, "Start")>]
    static member ``remove mask texture``(__instance: MaskedPlayerEnemy) =
        if not <| getConfig().enableMask then
            __instance.GetComponentsInChildren<Transform>()
                |> filter _.name.StartsWith("HeadMask")
                |> iter _.gameObject.SetActive(false)

    [<HarmonyPrefix>]
    [<HarmonyPatch(typeof<StartOfRound>, "EndOfGame")>]
    static member ``fix teleporter not working when masked enemy is spawned on player death``(__instance: StartOfRound) =
        // After a round is over, the player's dead body is still set.
        // This causes the teleporter to attempt to move the dead body, which always fails.
        flip iter __instance.allPlayerScripts <| fun player ->
            player.deadBody <- null