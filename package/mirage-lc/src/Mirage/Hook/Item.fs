module Mirage.Hook.Item

open FSharpPlus
open System.Collections.Generic

let private storeItems = List<Item>()

let populateItems () =
    On.Terminal.add_OnDisable(fun orig self ->
        orig.Invoke self
        storeItems.Clear()
    )

    On.Terminal.add_Start(fun orig self ->
        orig.Invoke self
        iter storeItems.Add self.buyableItemsList
    )