module Mirage.Hook.PlayerControllerB

open Mirage.Domain.Config

let hookPlayerControllerB () =
    On.GameNetcodeStuff.PlayerControllerB.add_ShowNameBillboard(fun orig self ->
        if not (isConfigReady()) || getConfig().enablePlayerNames then
            orig.Invoke self
    )