module Mirage.Unity.MaskedAnimator

open System
open UnityEngine
open Unity.Netcode
open Mirage.Domain.Null
open Mirage.Domain.Config

[<AllowNullLiteral>]
type MaskedAnimator() =
    inherit NetworkBehaviour()

    let random = Random()
    let mutable creatureAnimator = null
    let mutable itemHolder = GameObject "ItemHolder"
    let mutable heldItem: GrabbableObject = null
    let mutable upperBodyAnimationsWeight = 0.0f
    let mutable layerIndex = None

    let rec randomItem () =
        let items = StartOfRound.Instance.allItemsList.itemsList
        let item = items[random.Next(0, items.Count)]
        if isNull <| item.spawnPrefab.GetComponent<GrabbableObject>() then randomItem()
        else item.spawnPrefab

    let spawnItem () =
        let item =
            Object.Instantiate<GameObject>(
                randomItem(),
                Vector3.zero,
                Quaternion.identity
            )
        item.GetComponent<NetworkObject>().Spawn(destroyWithScene = true)
        let scanNode = item.GetComponentInChildren<ScanNodeProperties>()
        if isNotNull scanNode then
            scanNode.gameObject.SetActive false
        item.GetComponent<GrabbableObject>()

    member this.Start() =
        creatureAnimator <- this.transform.GetChild(0).GetChild(3).GetComponent<Animator>()
        layerIndex <- Some <| creatureAnimator.GetLayerIndex "Item"

        if layerIndex.IsNone then
            this.enabled <- false
            raise <| InvalidProgramException "Failed to find item layer. Please use a mod manager to install this mod properly."

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

    member this.HoldItem item =
        heldItem <- item
        heldItem.parentObject <- itemHolder.transform
        heldItem.isHeld <- true
        heldItem.isHeldByEnemy <- true
        heldItem.grabbable <- false
        if this.IsHost then
            this.HoldItemClientRpc item.NetworkObject
    
    [<ClientRpc>]
    member this.HoldItemClientRpc(reference: NetworkObjectReference) =
        if not this.IsHost then
            let mutable item = null
            if reference.TryGet &item then
                this.HoldItem <| item.GetComponent<GrabbableObject>()

    member _.FixedUpdate() =
        let reset name = creatureAnimator.ResetTrigger $"Hold{name}"
        let trigger name =
            reset "Shotgun"
            reset "Flash"
            reset "Lung"
            reset "OneItem"
            creatureAnimator.SetTrigger $"Hold{name}"

        if isNotNull heldItem then
            upperBodyAnimationsWeight <- Mathf.Lerp(upperBodyAnimationsWeight, 0.9f, 0.5f)
            creatureAnimator.SetLayerWeight(layerIndex.Value, upperBodyAnimationsWeight)

            if heldItem.itemProperties.twoHandedAnimation then
                trigger "Lung"
            else
                if heldItem :? FlashlightItem then
                    trigger "Flash"
                else if heldItem :? Shovel then
                    trigger "Lung"
                else if heldItem :? ShotgunItem then
                    trigger "Shotgun"
                else
                    trigger "OneItem"
    