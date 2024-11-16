module Mirage.Revive.Hook.Config

open Unity.Netcode
open Mirage.Revive.Domain.Config

let syncConfig () =
    On.GameNetcodeStuff.PlayerControllerB.add_ConnectClientToPlayerObject(fun orig self ->
        orig.Invoke self
        if NetworkManager.Singleton.IsHost then
            registerHandler RequestSync
        else
            registerHandler ReceiveSync
            requestSync()
    )

    On.GameNetworkManager.add_StartDisconnect(fun orig self ->
        orig.Invoke self
        revertSync()
    )