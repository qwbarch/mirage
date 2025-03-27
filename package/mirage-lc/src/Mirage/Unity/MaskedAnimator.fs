module Mirage.Unity.MaskedAnimator

open System
open UnityEngine
open Unity.Netcode
open Mirage.Domain.Null
open Mirage.Domain.Logger

[<AllowNullLiteral>]
type MaskedAnimator() =
    inherit NetworkBehaviour()

    let random = Random()
    let mutable creatureAnimator = null
    let mutable itemHolder = null
    let mutable heldItem = null
    let mutable upperBodyAnimationsWeight = 0.0f
    let mutable layerIndex = -1

    let rec randomItem () =
        let items = StartOfRound.Instance.allItemsList.itemsList
        let item = items[random.Next(0, items.Count)]
        if isNull <| item.spawnPrefab.GetComponent<GrabbableObject>() then randomItem()
        else item.spawnPrefab

    let spawnItem () =
        // if isHost
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
        layerIndex <- creatureAnimator.GetLayerIndex "Item"
        itemHolder <- GameObject "ItemHolder"
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

        heldItem <- spawnItem()
        heldItem.parentObject <- itemHolder.transform
        heldItem.isHeld <- true
        heldItem.isHeldByEnemy <- true
        heldItem.grabbable <- false

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
            creatureAnimator.SetLayerWeight(layerIndex, upperBodyAnimationsWeight)

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