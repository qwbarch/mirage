module Mirage.Hook.Dissonance

open Dissonance

let mutable private dissonance: DissonanceComms = null

let getDissonance () = dissonance

let cacheDissonance () =
    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        dissonance <- self
    )