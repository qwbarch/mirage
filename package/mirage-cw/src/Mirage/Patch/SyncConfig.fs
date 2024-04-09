module Mirage.Patch.SyncConfig

open HarmonyLib
open Mirage.Unity.ConfigHandler
open Mirage.Core.Field
open Mirage.Core.Config

type SyncConfig() =
    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<SurfaceNetworkHandler>, "Awake")>]
    static member ``add config handler to surface network handler``(__instance: SurfaceNetworkHandler) =
        __instance.gameObject.AddComponent<ConfigHandler>()

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<SurfaceNetworkHandler>, "RPCM_StartGame")>]
    static member ``sync config to clients on game start``(__instance: SurfaceNetworkHandler) =
        __instance.gameObject.GetComponent<ConfigHandler>().SyncToAllClients()

    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<MainMenuHandler>, "Start")>]
    static member ``revert synced config for all clients on game finish``() =
        setNone SyncedConfig