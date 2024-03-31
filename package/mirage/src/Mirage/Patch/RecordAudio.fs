module Mirage.Patch.RecordAudio

open Dissonance
open HarmonyLib

type RecordAudio() =
    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<DissonanceComms>, "Start")>]
    static member ``subscribe to microphone audio``(__instance: DissonanceComms) =
        ()