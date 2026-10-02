using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VRGIN.Core;

namespace KKCharaStudioVR;

// Bring the KK popup-capture fixes to the installed KKS VRGIN without replacing
// the XR framework DLL used by the main-game VR plugin.
[HarmonyPatch(typeof(VRGUI), "CatchCanvas")]
internal static class KksCanvasCapture
{
    private static readonly System.Reflection.FieldInfo BlockingMask = AccessTools.Field(typeof(GraphicRaycaster), "m_BlockingMask");
    internal static void Install() => new Harmony("Ermin.KKS.CanvasCapture").PatchAll(typeof(KksCanvasCapture));

    [HarmonyPrefix]
    private static bool Prefix(VRGUI __instance, object ____Registry, Camera ____VRGUICamera)
    {
        if (____VRGUICamera == null || !(____Registry is IDictionary registry)) return true;
        ____VRGUICamera.targetTexture = __instance.uGuiTexture;
        foreach (Canvas canvas in registry.Keys.Cast<Canvas>().Where(c => c != null).ToArray())
        {
            bool capture = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                || (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != ____VRGUICamera
                    && (canvas.worldCamera == null || canvas.worldCamera.targetTexture == null));
            if (!capture || VR.Interpreter.IsIgnoredCanvas(canvas)) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = ____VRGUICamera;
            int layer = LayerMask.NameToLayer(VR.Context.UILayer);
            if (layer >= 0)
                foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            if (VR.Context.EnforceDefaultGUIMaterials)
                foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true)) graphic.material = graphic.defaultMaterial;
            var old = canvas.GetComponent<GraphicRaycaster>();
            if (VR.Context.GUIAlternativeSortingMode && old != null && !(old is KksSortedRaycaster))
            {
                bool reversed = old.ignoreReversedGraphics, enabled = old.enabled;
                var blocking = old.blockingObjects;
                object mask = BlockingMask?.GetValue(old);
                Object.DestroyImmediate(old);
                var replacement = canvas.gameObject.AddComponent<KksSortedRaycaster>();
                replacement.ignoreReversedGraphics = reversed;
                replacement.blockingObjects = blocking;
                if (mask != null) BlockingMask.SetValue(replacement, mask);
                replacement.enabled = enabled;
            }
        }
        return false;
    }
}

internal sealed class KksSortedRaycaster : GraphicRaycaster
{
    private Canvas owner;
    private int Order => (owner != null ? owner : owner = GetComponent<Canvas>()).sortingOrder;
    public override int sortOrderPriority => Order;
    public override int renderOrderPriority => Order;
}
