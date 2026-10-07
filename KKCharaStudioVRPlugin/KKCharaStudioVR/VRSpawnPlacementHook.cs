using System;
#if KKS
using HarmonyLib;
using HarmonyInstance = KKCharaStudioVR.KksHarmony;
#else
using Harmony;
#endif
using Studio;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    public static class VRSpawnPlacementHook
    {
        public static void InstallHook()
        {
            // Auto-placement hooks are completely disabled so characters and props retain
            // Studio's native default coordinates (world origin 0,0,0) upon loading.
            // This ensures imported VMD cameras, dance animations, and scene layouts remain 100% aligned with world coordinates.
            VRLog.Info("[VRSpawnPlacementHook] Character & item auto-placement hooks are disabled to preserve native world origin alignment for VMD.");
        }

        public static void AddObjectFemalePostHook(OCICharFemale __result)
        {
            // Intentionally disabled: do not move characters on spawn.
        }

        public static void AddObjectMalePostHook(OCICharMale __result)
        {
            // Intentionally disabled: do not move characters on spawn.
        }

        public static void AddObjectItemPostHook(OCIItem __result)
        {
            // Intentionally disabled: do not move items on spawn.
        }
    }
}
