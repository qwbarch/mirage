module Mirage.Unity.MaskedAnimator

open System
open UnityEngine
open Unity.Netcode
open Mirage.Domain.Null

[<AllowNullLiteral>]
type MaskedAnimator() =
    inherit NetworkBehaviour()

    let random = Random()
    let mutable creatureAnimator = null
    let mutable itemHolder = null
    let mutable heldItem = null
    let mutable upperBodyAnimationsWeight = 0.0f
    let mutable updateFrequency = 0.0f

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
        let updateAnimation weight =
            upperBodyAnimationsWeight <- Mathf.Lerp(upperBodyAnimationsWeight, weight, 25f * updateFrequency)
            creatureAnimator.SetLayerWeight(creatureAnimator.GetLayerIndex "Item", upperBodyAnimationsWeight)

        updateAnimation <|
            if isNull heldItem then 0f
            else 0.9f

        creatureAnimator.SetTrigger "HoldFlash"
        creatureAnimator.ResetTrigger "HoldLung"
        creatureAnimator.ResetTrigger "HoldShotgun"
        creatureAnimator.ResetTrigger "HoldOneItem"