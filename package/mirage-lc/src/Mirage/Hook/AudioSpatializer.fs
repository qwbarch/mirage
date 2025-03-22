module Mirage.Hook.AudioSpatializer

open FSharpPlus
open UnityEngine
open Mirage.Domain.Null

/// Disables the audio spatializer log spam.
/// Credits: [IAmBatby](https://github.com/IAmBatby) and [mattymatty](https://github.com/mattymatty97).
let disableAudioSpatializer () =
    On.Unity.Netcode.NetworkSceneManager.add_OnSceneLoaded(fun orig self eventId ->
        orig.Invoke(self, eventId)
        let pluginName = AudioSettings.GetSpatializerPluginName()
        if isNull pluginName || pluginName = zero then
            for audioSource: AudioSource in Resources.FindObjectsOfTypeAll<AudioSource>() do
                audioSource.spatialize <- false
    )