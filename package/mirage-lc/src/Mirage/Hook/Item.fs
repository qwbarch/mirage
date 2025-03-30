module Mirage.Hook.Item

open FSharpPlus
open Mirage.Prelude
open Mirage.Domain.Config

let mutable private items = zero
let mutable private totalScrapWeight = 0

let getItems () = items
let getTotalScrapWeight () = totalScrapWeight

let populateItems () =
    On.StartOfRound.add_LoadShipGrabbableItems(fun orig self ->
        orig.Invoke self
        for item in self.allItemsList.itemsList do
            &items %= Map.add (stripConfigKey item.itemName) item
    )

    On.StartOfRound.add_OnDestroy(fun orig self ->
        orig.Invoke self
        items <- zero
        totalScrapWeight <- 0
    )

    On.StartOfRound.add_ChangeLevel(fun orig self levelId ->
        orig.Invoke(self, levelId)
        totalScrapWeight <-
            self.currentLevel.spawnableScrap
                |> map _.rarity
                |> sum
    )