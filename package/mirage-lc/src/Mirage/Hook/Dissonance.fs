module Mirage.Hook.Dissonance

open Dissonance

let mutable internal dissonance: DissonanceComms = null

let fetchDissonance () =
    On.Dissonance.DissonanceComms.add_Start(fun orig self ->
        orig.Invoke self
        dissonance <- self
    )