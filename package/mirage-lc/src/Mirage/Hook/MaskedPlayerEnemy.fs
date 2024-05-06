module Mirage.Hook.MaskedPlayerEnemy

open Mirage.Unity.MimicPlayer

let initMaskedEnemy () =
    // Normally, MimicPlayer.StartMimicking() gets run during MimicPlayer.Start().
    // To stay compatible with mods that mimic the player on startup (such as cosmetics),
    // this is run during MaskedPlayerEnemy.Start() instead.
    On.MaskedPlayerEnemy.add_Start(fun orig self ->
        orig.Invoke self
        self.GetComponent<MimicPlayer>().StartMimicking()
    )