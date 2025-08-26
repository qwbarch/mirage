module Mirage.Compatibility.LethalSettings

open LethalSettings
open System
open System.Collections.ObjectModel
open LethalSettings.UI.Components

/// LethalSettings has a compatibility issue where if one mod throws an exception,
/// all other mod's menus break.
/// 
/// This is an implementation of this pull request: https://github.com/willis81808/LethalSettings/pull/8
/// If the pull request is ever merged, this fix should be removed.
let fixLethalSettings() =
    On.LethalSettings.UI.ModMenu.add_BuildMod(fun orig self config ->
        try
            orig.Invoke(self, config)
        with | _ ->
            LethalSettingsPlugin.Log.LogWarning(
                $"Failed to run ModMenu.BuildMod(). Please contact the mod author to have it fixed.\n"
                    + $"Mod name: {config.Name}\n"
                    + $"Id: {config.Id}\n"
                    + $"Version: {config.Version}\n"
            );
    )

    // Ideally this should be using a transpiler, but I don't really care about doing it optimally right now.
    On.LethalSettings.UI.ModMenu.add_ShowModSettings(fun orig activeConfig availableConfigs ->
        for config in availableConfigs do
            if not (isNull config) && not (isNull config.Viewport) then
                let isActiveMod = Object.ReferenceEquals(config, activeConfig)
                let onMenu listener =
                    if not (isNull listener) then
                        config.OnMenuClose.Invoke(
                            config.Viewport,
                            ReadOnlyCollection<MenuComponent> config.MenuComponents
                        )

                // wasClosed.
                if config.Viewport.activeSelf && not isActiveMod then
                    onMenu config.OnMenuClose
                
                config.Viewport.SetActive isActiveMod
                config.ShowSettingsButton.ShowCaret <- isActiveMod

                // wasOpened.
                if not config.Viewport.activeSelf && isActiveMod then
                    onMenu config.OnMenuOpen
    )