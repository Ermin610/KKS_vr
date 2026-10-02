using HarmonyLib;
using Unity.XR.OpenVR;

namespace KKCharaStudioVR;

// The KKS upstream uses this override because scanning every loaded mod
// assembly for SteamVR_Input can fail on unrelated optional dependencies.
[HarmonyPatch(typeof(OpenVRHelpers), "IsUsingSteamVRInput")]
internal static class KksOpenVrInputPatch
{
    internal static void Install() => new Harmony("Ermin.KKS.OpenVRInput").PatchAll(typeof(KksOpenVrInputPatch));
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result) { __result = true; return false; }
}
