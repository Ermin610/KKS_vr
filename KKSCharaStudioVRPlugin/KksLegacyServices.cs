using System;
using BepInEx.Logging;

namespace BepInEx4
{
    // Keep the shared KK source's logging calls on the BepInEx 5 log pipeline.
    internal static class Logger
    {
        private static readonly ManualLogSource Source = BepInEx.Logging.Logger.CreateLogSource("KKS VR Overhaul");
        public static void Log(LogLevel level, object data) => Source.Log(level, data);
    }
}

namespace KKCharaStudioVR
{
    internal static class KksHarmony
    {
        public static HarmonyLib.Harmony Create(string id) => new HarmonyLib.Harmony(id);
    }
}
