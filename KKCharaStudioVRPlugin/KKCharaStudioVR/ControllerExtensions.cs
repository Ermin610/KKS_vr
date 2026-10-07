using UnityEngine;
using VRGIN.Controls;

namespace KKCharaStudioVR;

internal static class ControllerExtensions
{
    public static void SetRenderModelVisible(this Controller controller, bool visible)
    {
        if (controller == null || controller.Model == null)
            return;

        // VRGIN parents a SteamVR_RenderModel named "Model" under the controller.
        // That component loads the Vive wand / Touch / Cosmos mesh asynchronously,
        // then UpdateComponents reactivates each part from the OpenVR IsVisible
        // flag while updateDynamically is true. Dashboard focus also calls
        // SetMeshRendererState(true). A one-shot Renderer.enabled = false loses
        // to whichever of those happens later, so the stock mesh draws on top
        // of the custom hand. Stop the per-frame reactivation and force every
        // renderer under the model into the requested state.
        var model = controller.Model;
        model.updateDynamically = visible;
        ApplyRendererEnabled(((Component)model).transform, visible);
    }

    private static void ApplyRendererEnabled(Transform root, bool visible)
    {
        if (root == null)
            return;

        Renderer renderer = ((Component)root).GetComponent<Renderer>();
        if (renderer != null && renderer.enabled != visible)
            renderer.enabled = visible;

        int childCount = root.childCount;
        for (int i = 0; i < childCount; i++)
            ApplyRendererEnabled(root.GetChild(i), visible);
    }
}
