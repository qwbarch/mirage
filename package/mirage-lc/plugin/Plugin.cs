#pragma warning disable IDE0051 // Hide unused methods.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;

namespace Mirage.Plugin
{
    [BepInPlugin(PluginInfo.PLUGIN_ID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(Core.Plugin.PluginInfo.PLUGIN_ID)]
    class Plugin : BaseUnityPlugin
    {
        private static string directory;

        public static string Directory => directory;

        private void Awake()
        {
            directory = Path.GetDirectoryName(Info.Location);
            new Harmony(PluginInfo.PLUGIN_ID).PatchAll(typeof(InitializeMirage));
        }
    }

    class InitializeMirage
    {
        private static void LoadDependencies()
        {
            var dependencies = new string[]
            {
                "System.Threading.Channels.dll",
                "FSharp.Core.dll",
                "Collections.Pooled.dll",
                "IcedTasks.dll",
                "SileroVAD.dll",
                "FSharpPlus.dll"
            };

            foreach (var dependency in dependencies)
            {
                Assembly.LoadFrom($"{Core.Plugin.Plugin.Directory}/{dependency}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PreInitSceneScript), nameof(PreInitSceneScript.Start))]
        private static async void OnPreInitScene(PreInitSceneScript __instance)
        {
            await Task.Run(LoadDependencies);
            __instance.StartCoroutine(Main.main(
                Plugin.Directory,
                PluginInfo.PLUGIN_ID,
                PluginInfo.PLUGIN_NAME,
                PluginInfo.PLUGIN_VERSION
            ));
        }
    }
}