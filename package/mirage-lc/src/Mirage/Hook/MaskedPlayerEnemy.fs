module Mirage.Hook.MaskedPlayerEnemy

open FSharpPlus
open UnityEngine
open Mirage.Unity.MimicPlayer
open Mirage.Domain.Config

let hookMaskedEnemy () =
    On.MaskedPlayerEnemy.add_Start(fun orig self ->
        self.GetComponent<MimicPlayer>().StartMimicking()
        orig.Invoke self
        if not <| getConfig().enableMaskTexture then
            self.GetComponentsInChildren<Transform>()
                |> filter _.name.StartsWith("HeadMask")
                |> iter _.gameObject.SetActive(false)
    )

    On.MaskedPlayerEnemy.add_SetHandsOutClientRpc(fun orig self _ ->
        orig.Invoke(self, getConfig().enableArmsOut)
    )

    On.MaskedPlayerEnemy.add_SetHandsOutServerRpc(fun orig self _ ->
        orig.Invoke(self, getConfig().enableArmsOut)
    )

    On.StartOfRound.add_EndOfGame(fun orig self bodiesInsured connectedPlayersOnServer scrapCollected ->
        // After a round is over, the player's dead body is still set.
        // This causes the teleporter to attempt to move the dead body, which always fails.
        for player in self.allPlayerScripts do
            player.deadBody <- null
        orig.Invoke(self, bodiesInsured, connectedPlayersOnServer, scrapCollected)
    )