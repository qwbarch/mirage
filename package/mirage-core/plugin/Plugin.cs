#pragma warning disable IDE0051
#pragma warning disable CS1591

using System.IO;
using BepInEx;

namespace Mirage.Core.Plugin
{
    [BepInPlugin(PluginInfo.PLUGIN_ID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private static string directory;

        public static string Directory => directory;

        private void Awake() {
            directory = Path.GetDirectoryName(Info.Location);
        }
    }
}