module Mirage.Unity.MaskedAnimator

open IcedTasks
open System
open System.Threading.Tasks
open UnityEngine
open Unity.Netcode
open Mirage.Prelude
open Mirage.Domain.Null
open Mirage.Domain.Config
open Mirage.Compatibility
open Mirage.Hook.Item

/// Full credits goes to Piggy and VirusTLNR:
/// https://github.com/VirusTLNR/LethalIntelligence
[<AllowNullLiteral>]
type MaskedAnimator() =
    inherit NetworkBehaviour()

    let random = Random()
    let mutable creatureAnimator = null
    let mutable itemHolder = GameObject "ItemHolder"
    let mutable upperBodyAnimationsWeight = 0.0f
    let mutable layerIndex = -1
    let mutable dropItemOnDeath = false

    let rec chooseItem () =
        let roll = random.NextDouble() * 100.0
        let mutable cumulativePercent = 0.0
        if random.Next(0, 100) < getConfig().storeItemRollChance && not (Map.isEmpty <| getConfig().storeItemWeights) then
            let mutable itemName = null
            for weight in getConfig().storeItemWeights do
                if isNull itemName then
                    &cumulativePercent += float weight.Value / float (getConfig().totalItemWeights) * 100.0
                    if roll < cumulativePercent then
                        itemName <- stripConfigKey weight.Key
            Map.find itemName <| getItems()
        else
            let mutable item = null
            for scrap in StartOfRound.Instance.currentLevel.spawnableScrap do
                if isNull item then
                    &cumulativePercent += float scrap.rarity / float (getTotalScrapWeight()) * 100.0
                    if not (Set.contains (stripConfigKey scrap.spawnableItem.itemName) <| getConfig().disabledScrapItems) && roll < cumulativePercent then
                        item <- scrap.spawnableItem
            item

    let spawnItem () =
        let item = chooseItem()
        let prefab =
            Object.Instantiate<GameObject>(
                item.spawnPrefab,
                Vector3.zero,
                Quaternion.identity
            )
        prefab.GetComponent<NetworkObject>().Spawn(destroyWithScene = true)
        struct (
            prefab.GetComponent<GrabbableObject>(),
            int <|
                float (random.Next(item.minValue, item.maxValue))
                    * float RoundManager.Instance.scrapValueMultiplier
                    * getConfig().scrapValueMultiplier
        )
    
    member val HeldItem: GrabbableObject = null with get, set

    member _.DropItemOnDeath with get() = dropItemOnDeath

    member this.Start() =
        if isLethalIntelligenceLoaded() then
            this.enabled <- false
        else
            creatureAnimator <- this.transform.GetChild(0).GetChild(3).GetComponent<Animator>()
            layerIndex <- creatureAnimator.GetLayerIndex "Item"

            itemHolder.transform.parent <-
                this.transform
                    .GetChild(0)
                    .GetChild(3)
                    .GetChild(0)
                    .GetChild(0)
                    .GetChild(0)
                    .GetChild(0)
                    .GetChild(1)
                    .GetChild(0)
                    .GetChild(0)
                    .GetChild(0)
            itemHolder.transform.localPosition <- Vector3(-0.002f, 0.036f, -0.042f);
            itemHolder.transform.localRotation <- Quaternion.Euler(-3.616f, -2.302f, 0.145f)

            if this.IsHost && random.Next(0, 100) < getConfig().maskedItemSpawnChance then
                this.HoldItem <| spawnItem()
    
    override this.OnNetworkSpawn() =
        base.OnNetworkSpawn()

    member this.HoldItem struct (item, scrapValue) =
        this.HeldItem <- item
        this.HeldItem.SetScrapValue scrapValue

        // Disable scanner text.
        let scanNode = item.GetComponentInChildren<ScanNodeProperties>()
        if isNotNull scanNode then
            scanNode.gameObject.SetActive false

        // Hide the hover text.
        let collider = item.GetComponent<BoxCollider>()
        if isNotNull collider then
            collider.enabled <- false

        this.HeldItem.isHeldByEnemy <- true
        this.HeldItem.grabbable <- false
        this.HeldItem.grabbableToEnemies <- false
        this.HeldItem.hasHitGround <- false
        this.HeldItem.EnablePhysics false
        this.HeldItem.parentObject <- itemHolder.transform

        if this.IsHost then
            let isScrap = this.HeldItem.itemProperties.isScrap
            dropItemOnDeath <-
                isScrap && Random().Next(0, 100) < getConfig().maskedDropScrapItemOnDeath
                    || not isScrap && Random().Next(0, 100) < getConfig().maskedDropStoreItemOnDeath

            this.HoldItemClientRpc(item.NetworkObject, scrapValue, dropItemOnDeath)
    
    [<ClientRpc>]
    member this.HoldItemClientRpc(reference: NetworkObjectReference, scrapValue, dropItemOnDeath') =
        if not this.IsHost then
            let mutable item = null
            if reference.TryGet &item then
                this.HoldItem <| struct (item.GetComponent<GrabbableObject>(), scrapValue)
                dropItemOnDeath <- dropItemOnDeath'

    member this.FixedUpdate() =
        let reset name = creatureAnimator.ResetTrigger("Hold" + name)
        let trigger name =
            reset "Shotgun"
            reset "Flash"
            reset "Lung"
            reset "OneItem"
            creatureAnimator.SetTrigger("Hold" + name)

        if isNotNull this.HeldItem then
            upperBodyAnimationsWeight <- Mathf.Lerp(upperBodyAnimationsWeight, 0.9f, 0.5f)
            creatureAnimator.SetLayerWeight(layerIndex, upperBodyAnimationsWeight)

            trigger <|
                if this.HeldItem.itemProperties.twoHandedAnimation then
                    "Lung"
                else if this.HeldItem :? FlashlightItem then
                    "Flash"
                else if this.HeldItem :? Shovel then
                    "Lung"
                else if this.HeldItem :? ShotgunItem then
                    "Shotgun"
                else
                    "OneItem"
    
    member this.OnDeath() =
        let heldItem = this.HeldItem
        if isNotNull heldItem && this.DropItemOnDeath then
            ignore <| valueTask {
                do! Task.Delay 900 // Wait for the masked enemy to fall to the ground.

                &RoundManager.Instance.totalScrapValueInLevel += float32 heldItem.scrapValue

                heldItem.isHeldByEnemy <- false
                heldItem.hasHitGround <- true
                heldItem.EnablePhysics true

                heldItem.grabbable <- true
                heldItem.grabbableToEnemies <- true

                // Enable scanner.
                let scanNode = heldItem.transform.GetComponentInChildren<ScanNodeProperties> true
                if isNotNull scanNode then
                    scanNode.gameObject.SetActive true

                // Enable the hover text.
                let collider = heldItem.GetComponent<BoxCollider>()
                if isNotNull collider then
                    collider.enabled <- true

                this.HeldItem <- null
            }