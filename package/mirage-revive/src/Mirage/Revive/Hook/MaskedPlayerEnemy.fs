module Mirage.Revive.Hook.MaskedPlayerEnemy

open System
open System.Collections
open System.Collections.Generic
open GameNetcodeStuff
open UnityEngine
open UnityEngine.AI
open Unity.Netcode
open Mirage.Revive.Domain.Config
open Mirage.Revive.Unity.BodyDeactivator
open Mirage.Revive.Domain.Logger

let private random = Random()

/// Killed players are tracked to prevent spawning multiple masked enemies when the player dies, since one of the hooks runs multiple times.
let private killedPlayers = new HashSet<uint64>()

let mutable private maskedEnemyPrefab = null

let private spawnMaskedEnemy (player: PlayerControllerB) causeOfDeath deathAnimation spawnBody bodyVelocity =
    if killedPlayers.Add player.playerClientId then
        let playerPosition = player.transform.position
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
        let isPlayerAloneAndRequired = not config.reviveOnlyWhenPlayerAlone || player.isPlayerAlone
        let spawnRateSuccess () = random.Next(1, 101) <= config.reviveChance

        // isOnNavMesh is false while on the ship.
        if (isOnNavMesh || player.isInHangarShipRoom && StartOfRound.Instance.shipHasLanded)
            && not playerKilledByMaskItem
            && not playerKilledByMaskedEnemy
            && spawnBody
            && isPlayerAloneAndRequired
            && spawnRateSuccess()
        then
            let rotationY = player.transform.eulerAngles.y
            let maskedEnemy =
                Object.Instantiate<GameObject>(
                    maskedEnemyPrefab,
                    player.transform.position,
                    Quaternion.Euler <| Vector3(0f, rotationY, 0f)
                )
            let enemyAI = maskedEnemy.GetComponent<MaskedPlayerEnemy>()
            enemyAI.mimickingPlayer <- player
            maskedEnemy.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene = true)
            player.GetComponent<BodyDeactivator>().DeactivateBody enemyAI

let revivePlayersOnDeath () =
    On.GameNetcodeStuff.PlayerControllerB.add_Awake(fun orig self -> 
        orig.Invoke self
        ignore <| self.gameObject.AddComponent<BodyDeactivator>()
    )

    // Save the haunted mask item prefab.
    On.GameNetworkManager.add_Start(fun orig self ->
        orig.Invoke self
        for prefab in self.GetComponent<NetworkManager>().NetworkConfig.Prefabs.m_Prefabs do
            let maskedEnemy = prefab.Prefab.gameObject.GetComponent<MaskedPlayerEnemy>()
            // enemyName must be matched to avoid mods that extend from MaskedPlayerEnemy.
            if not <| isNull maskedEnemy && maskedEnemy.enemyType.enemyName = "MaskedPlayerEnemy" then
                maskedEnemyPrefab <- maskedEnemy.gameObject
        if isNull maskedEnemyPrefab then
            logWarning "HauntedMaskItem prefab is missing. Another mod is messing with this prefab when they shouldn't be."
    )

    // Reset the killed players in between rounds.
    On.StartOfRound.add_StartGame(fun orig self ->
        orig.Invoke self
        killedPlayers.Clear()
    )

    // Spawn a masked enemy on player death.
    On.GameNetcodeStuff.PlayerControllerB.add_KillPlayerServerRpc(fun orig self playerId spawnBody bodyVelocity causeOfDeath deathAnimation positionOffset ->
        orig.Invoke(self, playerId, spawnBody, bodyVelocity, causeOfDeath, deathAnimation, positionOffset)
        spawnMaskedEnemy self causeOfDeath deathAnimation spawnBody bodyVelocity
    )
    On.GameNetcodeStuff.PlayerControllerB.add_KillPlayer(fun orig self bodyVelocity spawnBody causeOfDeath deathAnimation positionOffset ->
        orig.Invoke(self, bodyVelocity, spawnBody, causeOfDeath, deathAnimation, positionOffset)
        spawnMaskedEnemy self (int causeOfDeath) deathAnimation spawnBody bodyVelocity
    )

    // After a round is over, the player's dead body is still set.
    // This causes the teleporter to attempt to move the dead body, which always fails.
    // The following hook fixes this by ensuring deadBody is null in between rounds.
    On.StartOfRound.add_EndOfGame(fun orig self bodiesEnsured connectedPlayersOnServer scrapCollected ->
        seq {
            yield orig.Invoke(self, bodiesEnsured, connectedPlayersOnServer, scrapCollected)
            for player in self.allPlayerScripts do
                player.deadBody <- null
        } :?> IEnumerator
    )