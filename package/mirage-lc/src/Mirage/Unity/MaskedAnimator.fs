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
open Mirage.Domain.Logger

/// Full credits goes to Piggy and VirusTLNR:
/// https://github.com/VirusTLNR/LethalIntelligence
[<AllowNullLiteral>]
type MaskedAnimator() as self =
    inherit NetworkBehaviour()

    let random = Random()
    let mutable creatureAnimator = null
    let mutable itemHolder = GameObject "LocalItemHolder"
    let mutable upperBodyAnimationsWeight = 0.0f
    let mutable layerIndex = -1
    let mutable dropItemOnDeath = false

    let itemHolderPositionAndRotation = function
        | "HoldKnife" | "Grab" | "HoldPatcherTool" ->
            struct(
                Vector3(0.002f, 0.056f, -0.046f),
                Quaternion.Euler(352.996f, 0f, 356.89f)
            )
        | "HoldLung" ->
            struct (
                Vector3(-0.1799f, -0.1014f, -0.5119f),
                Quaternion.Euler(314.149f, 13.06f, 305.192f)
            )
        | "HoldShotgun" ->
            struct (
                Vector3(0.0402f, -0.048f, 0.0104f),
                Quaternion.Euler(340.836f, 22.626f, 2.104f)
            )
        | "HoldJetpack" ->
            struct (
                Vector3(-0.107f, -0.078f, -0.232f),
                Quaternion.Euler(305.184f, 210.613f, 84.047f)
            )
        | "HoldForward" ->
            struct (
                Vector3(-0.1453f, -0.0898f, -0.4686f),
                Quaternion.Euler(314.149f, 13.06f, 305.192f)
            )
        | "GrabClipboard" ->
            struct (
                Vector3(0.007f, 0.138f, -0.066f),
                Quaternion.Euler(15.577f, 358.759f, 356.795f)
            )
        | _ as unknownAnimation ->
            logWarning $"Unsupported held item animation. Item: {self.HeldItem.itemProperties.itemName}. Animation: {unknownAnimation}"
            // Empty-handed default position/rotation.
            struct (
                Vector3(0.002f, 0.0563f, -0.0456f),
                Quaternion.Euler(359.9778f, -0.0006f, 359.9901f)
            )

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
            layerIndex <- creatureAnimator.GetLayerIndex "Held Item"

            itemHolder.transform.parent <-
                this.transform
                    .GetChild(0) // ScavengerModel
                    .GetChild(3) // metarig
                    .GetChild(0) // spine
                    .GetChild(0) // spine.001
                    .GetChild(0) // spine.002
                    .GetChild(0) // spine.003
                    .GetChild(1) // shoulder.R
                    .GetChild(0) // arm.R_upper
                    .GetChild(0) // arm.R_lower
                    .GetChild(0) // hand.R

            if this.IsHost && random.Next(0, 100) < getConfig().maskedItemSpawnChance then
                this.HoldItem <| spawnItem()
    
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

        // Set the held animation.
        let animation =
            if String.IsNullOrEmpty this.HeldItem.itemProperties.grabAnim then
                "Grab"
            else
                this.HeldItem.itemProperties.grabAnim
        creatureAnimator.SetBool(animation, true)

        let struct (localPosition, localRotation) = itemHolderPositionAndRotation animation
        itemHolder.transform.localPosition <- localPosition
        itemHolder.transform.localRotation <- localRotation
    
        this.HeldItem.isHeldByEnemy <- true
        this.HeldItem.grabbable <- false
        this.HeldItem.grabbableToEnemies <- false
        this.HeldItem.hasHitGround <- false
        this.HeldItem.EnablePhysics false
        this.HeldItem.parentObject <- itemHolder.transform

        if this.IsHost then
            // Roll for whether to drop item on death or not.
            let isScrap = this.HeldItem.itemProperties.isScrap
            dropItemOnDeath <-
                isScrap && random.Next(0, 100) < getConfig().maskedDropScrapItemOnDeath
                    || not isScrap && random.Next(0, 100) < getConfig().maskedDropStoreItemOnDeath
            
            // Roll for whether to emit light or not (if it's a flashlight).
            let emitFlashlight = random.Next(0, 100) < getConfig().emitFlashlightChance
            if emitFlashlight then
                this.EmitFlashlight()

            this.HoldItemClientRpc(item.NetworkObject, scrapValue, dropItemOnDeath, emitFlashlight)
    
    member private this.EmitFlashlight() =
        if this.HeldItem :? FlashlightItem then
            for light in this.HeldItem.GetComponentsInChildren<Light>() do
                light.enabled <- true
    
    [<ClientRpc>]
    member this.HoldItemClientRpc(reference: NetworkObjectReference, scrapValue, dropItemOnDeath', emitFlashlight) =
        if not this.IsHost then
            let mutable item = null
            if reference.TryGet &item then
                this.HoldItem <| struct (item.GetComponent<GrabbableObject>(), scrapValue)
                dropItemOnDeath <- dropItemOnDeath'
                if emitFlashlight then
                    this.EmitFlashlight()

    member this.FixedUpdate() =
        if isNotNull this.HeldItem then
            upperBodyAnimationsWeight <- Mathf.Lerp(upperBodyAnimationsWeight, 0.9f, 0.5f)
            creatureAnimator.SetLayerWeight(layerIndex, upperBodyAnimationsWeight)
    
    member this.OnDeath() =
        let heldItem = this.HeldItem
        if isNotNull heldItem && this.DropItemOnDeath then
            ignore <| valueTask {
                do! Task.Delay 900 // Wait for the masked enemy to fall to the ground.

                &RoundManager.Instance.totalScrapValueInLevel += float32 heldItem.scrapValue

                heldItem.isHeldByEnemy <- false
                heldItem.hasHitGround <- true
                heldItem.EnablePhysics true

                // Disable the flashlight light.
                if heldItem :? FlashlightItem then
                    for light in heldItem.GetComponentsInChildren<Light>() do
                        light.enabled <- false

                // Enable scanner.
                let scanNode = heldItem.transform.GetComponentInChildren<ScanNodeProperties> true
                if isNotNull scanNode then
                    scanNode.gameObject.SetActive true

                // Enable the hover text.
                let collider = heldItem.GetComponent<BoxCollider>()
                if isNotNull collider then
                    collider.enabled <- true

                heldItem.grabbable <- true
                heldItem.grabbableToEnemies <- true

                this.HeldItem <- null
            }