module Mirage.Patch.AudioSpatializer

open FSharpPlus
open HarmonyLib
open UnityEngine
open Unity.Netcode

type AudioSpatializer() =
    [<HarmonyPostfix>]
    [<HarmonyPatch(typeof<NetworkSceneManager>, "OnSceneLoaded")>]
    static member ``hide audio spatializer warning log spam``() =
        let pluginName = AudioSettings.GetSpatializerPluginName()
        if isNull pluginName || pluginName = zero then
            let disableSpatialize (audioSource: AudioSource) = audioSource.spatialize <- false
            iter disableSpatialize <| Resources.FindObjectsOfTypeAll<AudioSource>()