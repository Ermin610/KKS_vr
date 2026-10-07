using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRGIN.Core;
using VRGIN.Visuals;
using Object = UnityEngine.Object;

namespace KKCharaStudioVR;

// Screen-space dialogs, error popups and mod overlays (AutoTranslator, Message
// Center, Configuration Manager's uGUI hosts) must render into VRGIN's GUI
// camera. Anything left on the game view is covered by the black desktop camera,
// and anything left on the XR camera is clipped by the headset near plane.
[HarmonyPatch(typeof(VRGUI), "CatchCanvas")]
internal static class KksCanvasCapture
{
    private const float PlaneDistance = 100f;
    private const int LayerRefreshFrames = 45;

    private static readonly FieldInfo BlockingMask = AccessTools.Field(typeof(GraphicRaycaster), "m_BlockingMask");
    private static readonly FieldInfo UGuiTexture = AccessTools.Field(typeof(VRGUI), "<uGuiTexture>k__BackingField");
    private static readonly FieldInfo ImGuiTexture = AccessTools.Field(typeof(VRGUI), "<IMGuiTexture>k__BackingField");

    private static readonly List<Canvas> Tracked = new List<Canvas>();
    private static readonly List<Canvas> RegistryScan = new List<Canvas>();
    private static readonly HashSet<int> Logged = new HashSet<int>();
    private static readonly HashSet<int> Layered = new HashSet<int>();

    private const float CardTextScale = 2.4f;

    private static Camera _guiCamera;
    private static int _layerRefresh;
    private static bool _applying;
    private static bool _resizeFieldsMissing;
    private static bool _pickerTypesCached;
    private static bool _folderPatchInstalled;
    private static bool _folderPatchGaveUp;
    private static float _nextFolderPatchTry;
    private static Type _sceneLoadType;
    private static Type _checkSceneType;
    private static Type _tmpTextType;
    private static PropertyInfo _tmpFontSize;
    private static PropertyInfo _tmpAutoSize;
    private static bool _tmpChecked;
    private static readonly HashSet<int> ScaledLabels = new HashSet<int>();

    internal static void Install()
    {
        Harmony harmony = new Harmony("Ermin.KKS.CanvasCapture");
        harmony.PatchAll(typeof(KksCanvasCapture));
        EnsureFolderPickerText(harmony);
        if (KksCanvasCaptureDriver.Instance == null)
        {
            GameObject go = new GameObject("KKVR_CanvasCapture");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<KksCanvasCaptureDriver>();
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(VRGUI __instance, object ____Registry, Camera ____VRGUICamera)
    {
        if (____VRGUICamera == null || !(____Registry is IDictionary registry))
            return true;

        _guiCamera = ____VRGUICamera;
        EnsureTargets(__instance, ____VRGUICamera);
        ____VRGUICamera.targetTexture = __instance.uGuiTexture;

        RegistryScan.Clear();
        foreach (object key in registry.Keys)
        {
            if (key is Canvas canvas && canvas != null)
                RegistryScan.Add(canvas);
        }
        for (int i = 0; i < RegistryScan.Count; i++)
            Consider(RegistryScan[i], ____VRGUICamera);
        return false;
    }

    internal static void LateMaintain()
    {
        Camera guiCamera = _guiCamera;
        if (guiCamera == null)
            return;

        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            Canvas canvas = Tracked[i];
            if (canvas == null)
            {
                Tracked.RemoveAt(i);
                continue;
            }
            if (!canvas.gameObject.activeInHierarchy)
                continue;
            // A popup script that forces Overlay in LateUpdate would otherwise
            // draw into the desktop view, which the cover camera then blacks out.
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != guiCamera)
                Apply(canvas, guiCamera);
            else
                ClampPlane(canvas, guiCamera);
            EnlargeCardPickerText(canvas);
        }

        if (!_folderPatchInstalled)
            EnsureFolderPickerText(null);

        if (--_layerRefresh > 0)
            return;
        _layerRefresh = LayerRefreshFrames;
        // New popup children inherit Default unless the walk runs again.
        Layered.Clear();
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
        for (int i = 0; i < canvases.Length; i++)
            Consider(canvases[i], guiCamera);
    }

    private static void Consider(Canvas canvas, Camera guiCamera)
    {
        if (canvas == null || guiCamera == null || _applying)
            return;
        if (VR.Interpreter != null && VR.Interpreter.IsIgnoredCanvas(canvas))
            return;
        if (IsOwnedWorldUi(canvas))
            return;

        bool already = canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == guiCamera;
        if (already)
        {
            ClampPlane(canvas, guiCamera);
            Remember(canvas);
            int id = canvas.GetInstanceID();
            if (!Layered.Contains(id))
                ApplyLayer(canvas);
            EnlargeCardPickerText(canvas);
            return;
        }

        if (!IsScreenCanvas(canvas, guiCamera) && !IsStrayScreenCanvas(canvas))
            return;
        Apply(canvas, guiCamera);
    }

    private static bool IsScreenCanvas(Canvas canvas, Camera guiCamera)
    {
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return true;
        if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera == guiCamera)
            return false;
        Camera cam = canvas.worldCamera;
        if (cam == null || cam.targetTexture == null)
            return true;
        // An XR camera with an eye texture draws this canvas into the headset,
        // at planeDistance, where the near clip throws it away.
        return cam.stereoTargetEye != StereoTargetEyeMask.None;
    }

    // World-space canvases that were built as flat screen UI (Scale With Screen
    // Size, or a large panel parented to a camera) sit at the origin or on the
    // HMD and never appear on the GUI quad.
    private static bool IsStrayScreenCanvas(Canvas canvas)
    {
        if (canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas)
            return false;
        if (canvas.GetComponent<GraphicRaycaster>() == null)
            return false;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        bool screenScaled = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize;
        RectTransform rect = canvas.GetComponent<RectTransform>();
        bool big = rect != null && rect.rect.width >= 400f && rect.rect.height >= 200f;
        bool onCamera = canvas.transform.parent != null && canvas.transform.parent.GetComponent<Camera>() != null;
        return screenScaled || (onCamera && big);
    }

    private static bool IsOwnedWorldUi(Canvas canvas)
    {
        if (canvas.renderMode != RenderMode.WorldSpace)
            return false;
        Transform cursor = canvas.transform;
        for (int i = 0; i < 8 && cursor != null; i++)
        {
            string name = cursor.name;
            if (name.IndexOf("KKVR_", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("VRGIN", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("GUIQuad", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("ToolIcon", System.StringComparison.Ordinal) >= 0)
                return true;
            cursor = cursor.parent;
        }
        return false;
    }

    private static void Apply(Canvas canvas, Camera guiCamera)
    {
        _applying = true;
        try
        {
            RenderMode previous = canvas.renderMode;
            float previousDistance = canvas.planeDistance;
            // A world canvas parented to the HMD camera stays glued to the
            // headset after the mode change and is clipped by the near plane.
            if (previous == RenderMode.WorldSpace
                && canvas.transform.parent != null
                && canvas.transform.parent.GetComponent<Camera>() != null)
                canvas.transform.SetParent(null, true);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = guiCamera;
            ClampPlane(canvas, guiCamera);
            ApplyLayer(canvas);
            if (VR.Context != null && VR.Context.EnforceDefaultGUIMaterials)
            {
                Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                    graphics[i].material = graphics[i].defaultMaterial;
            }
            ReplaceRaycaster(canvas);
            Remember(canvas);
            EnlargeCardPickerText(canvas);
            LogOnce(canvas, "GUI capture: " + canvas.name
                + " " + previous + " -> ScreenSpaceCamera, plane "
                + previousDistance.ToString("F0") + " -> " + canvas.planeDistance.ToString("F0"));
        }
        finally
        {
            _applying = false;
        }
    }

    private static void ClampPlane(Canvas canvas, Camera guiCamera)
    {
        float max = guiCamera.farClipPlane - 1f;
        float min = 0.5f;
        if (max < min + 1f)
            max = min + 10f;
        float distance = Mathf.Clamp(PlaneDistance, min, max);
        if (guiCamera.farClipPlane < distance + 5f)
            guiCamera.farClipPlane = distance + 50f;
        if (!Mathf.Approximately(canvas.planeDistance, distance))
            canvas.planeDistance = distance;
    }

    private static void ApplyLayer(Canvas canvas)
    {
        if (VR.Context == null)
            return;
        int layer = LayerMask.NameToLayer(VR.Context.UILayer);
        if (layer < 0)
            return;
        Transform[] children = canvas.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
            children[i].gameObject.layer = layer;
        Layered.Add(canvas.GetInstanceID());
    }

    private static void ReplaceRaycaster(Canvas canvas)
    {
        if (VR.Context == null || !VR.Context.GUIAlternativeSortingMode)
            return;
        GraphicRaycaster old = canvas.GetComponent<GraphicRaycaster>();
        if (old == null || old is KksSortedRaycaster)
            return;
        bool reversed = old.ignoreReversedGraphics;
        bool enabled = old.enabled;
        GraphicRaycaster.BlockingObjects blocking = old.blockingObjects;
        object mask = BlockingMask?.GetValue(old);
        Object.DestroyImmediate(old);
        KksSortedRaycaster replacement = canvas.gameObject.AddComponent<KksSortedRaycaster>();
        replacement.ignoreReversedGraphics = reversed;
        replacement.blockingObjects = blocking;
        if (mask != null)
            BlockingMask.SetValue(replacement, mask);
        replacement.enabled = enabled;
    }

    private static void Remember(Canvas canvas)
    {
        for (int i = 0; i < Tracked.Count; i++)
        {
            if (Tracked[i] == canvas)
                return;
        }
        Tracked.Add(canvas);
    }

    private static void LogOnce(Canvas canvas, string message)
    {
        if (!Logged.Add(canvas.GetInstanceID()))
            return;
        VRLog.Info(message);
    }

    private static void EnsureTargets(VRGUI gui, Camera guiCamera)
    {
        int width = Screen.width;
        int height = Screen.height;
        if (width < 16 || height < 16)
            return;

        bool sizeMatches = gui.uGuiTexture != null && gui.IMGuiTexture != null
            && gui.uGuiTexture.width == width && gui.uGuiTexture.height == height
            && gui.IMGuiTexture.width == width && gui.IMGuiTexture.height == height;
        if (!sizeMatches)
        {
            if (UGuiTexture == null || ImGuiTexture == null)
            {
                if (!_resizeFieldsMissing)
                {
                    _resizeFieldsMissing = true;
                    VRLog.Warn("GUI capture resize skipped: texture backing fields are missing.");
                }
            }
            else
            {
                RenderTexture previousUi = gui.uGuiTexture;
                RenderTexture previousIm = gui.IMGuiTexture;
                RenderTexture ui = CreateTexture(width, height, 24);
                RenderTexture im = CreateTexture(width, height, 0);
                UGuiTexture.SetValue(gui, ui);
                ImGuiTexture.SetValue(gui, im);
                guiCamera.targetTexture = ui;
                if (previousUi != null)
                    Object.Destroy(previousUi);
                if (previousIm != null)
                    Object.Destroy(previousIm);
                RefreshQuads();
                VRLog.Info("GUI capture target resized to " + width + "x" + height);
            }
        }

        if (!guiCamera.orthographic)
            return;
        float halfHeight = height * 0.5f;
        Vector3 position = guiCamera.transform.position;
        if (Mathf.Abs(guiCamera.orthographicSize - halfHeight) > 0.5f
            || Mathf.Abs(position.x - width * 0.5f) > 0.5f
            || Mathf.Abs(position.y - halfHeight) > 0.5f)
        {
            guiCamera.orthographicSize = halfHeight;
            guiCamera.transform.position = new Vector3(width * 0.5f, halfHeight, -1f);
        }
    }

    private static RenderTexture CreateTexture(int width, int height, int depth)
    {
        RenderTexture texture = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32);
        texture.useMipMap = false;
        texture.autoGenerateMips = false;
        texture.Create();
        return texture;
    }

    private static void RefreshQuads()
    {
        GUIQuad[] quads = Resources.FindObjectsOfTypeAll<GUIQuad>();
        for (int i = 0; i < quads.Length; i++)
        {
            GUIQuad quad = quads[i];
            if (quad == null || !quad.gameObject.scene.IsValid())
                continue;
            quad.UpdateGUI();
        }
    }

    private static void EnsureFolderPickerText(Harmony harmony)
    {
        if (_folderPatchInstalled || _folderPatchGaveUp)
            return;
        if (harmony == null && Time.realtimeSinceStartup < _nextFolderPatchTry)
            return;
        _nextFolderPatchTry = Time.realtimeSinceStartup + 1f;
        if (harmony == null && Time.realtimeSinceStartup > 60f)
        {
            _folderPatchGaveUp = true;
            return;
        }

        Type sceneFolders = AccessTools.TypeByName("BrowserFolders.Hooks.KKS.SceneFolders");
        if (sceneFolders == null)
            return;

        MethodInfo onGui = AccessTools.Method(sceneFolders, "OnGui");
        MethodInfo window = AccessTools.Method(
            typeof(GUILayout),
            "Window",
            new[]
            {
                typeof(int),
                typeof(Rect),
                typeof(GUI.WindowFunction),
                typeof(string),
                typeof(GUILayoutOption[])
            });
        if (onGui == null || window == null)
        {
            _folderPatchGaveUp = true;
            VRLog.Warn("Scene folder text scale skipped: OnGui or GUILayout.Window was not found.");
            return;
        }

        if (harmony == null)
            harmony = new Harmony("Ermin.KKS.CanvasCapture.Folders");
        try
        {
            harmony.Patch(
                window,
                prefix: new HarmonyMethod(typeof(KksFolderPickerText), nameof(KksFolderPickerText.Widen)));
            harmony.Patch(
                onGui,
                prefix: new HarmonyMethod(typeof(KksFolderPickerText), nameof(KksFolderPickerText.Begin)),
                postfix: new HarmonyMethod(typeof(KksFolderPickerText), nameof(KksFolderPickerText.End)));
            _folderPatchInstalled = true;
            VRLog.Info("Scene folder picker text scale installed.");
        }
        catch (Exception ex)
        {
            _folderPatchGaveUp = true;
            VRLog.Warn("Scene folder text scale skipped: " + ex.Message);
        }
    }

    private static bool IsCardPickerCanvas(Canvas canvas)
    {
        if (canvas == null)
            return false;
        string sceneName = canvas.gameObject.scene.name;
        if (sceneName == "StudioSceneLoad" || sceneName == "StudioCheck")
            return true;
        string name = canvas.name;
        if (name != null && name.IndexOf("Canvas Load", StringComparison.Ordinal) >= 0)
            return true;
        if (!_pickerTypesCached)
        {
            _pickerTypesCached = true;
            _sceneLoadType = AccessTools.TypeByName("Studio.SceneLoadScene");
            _checkSceneType = AccessTools.TypeByName("Studio.CheckScene");
        }
        return HasBehaviour(canvas, _sceneLoadType) || HasBehaviour(canvas, _checkSceneType);
    }

    private static bool HasBehaviour(Canvas canvas, Type type)
    {
        if (type == null)
            return false;
        Transform cursor = canvas.transform;
        for (int i = 0; i < 6 && cursor != null; i++)
        {
            if (cursor.GetComponent(type) != null)
                return true;
            cursor = cursor.parent;
        }
        return canvas.GetComponentInChildren(type, true) != null;
    }

    private static void EnlargeCardPickerText(Canvas canvas)
    {
        if (!IsCardPickerCanvas(canvas))
            return;

        int changed = 0;
        Text[] labels = canvas.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            Text label = labels[i];
            if (label == null || !ScaledLabels.Add(label.GetInstanceID()))
                continue;
            int basis = label.fontSize > 0 ? label.fontSize : 14;
            int next = Mathf.Clamp(Mathf.RoundToInt(basis * CardTextScale), 28, 44);
            label.resizeTextForBestFit = false;
            if (label.fontSize < next)
                label.fontSize = next;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            changed++;
        }
        changed += EnlargeTmpLabels(canvas);
        if (changed <= 0)
            return;

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        string scalerInfo = scaler == null
            ? "no scaler"
            : scaler.uiScaleMode
                + " ref " + scaler.referenceResolution.x.ToString("F0")
                + "x" + scaler.referenceResolution.y.ToString("F0")
                + " factor " + scaler.scaleFactor.ToString("F2");
        VRLog.Info("Card picker text scaled on " + canvas.name
            + " (" + changed + " labels, " + scalerInfo + ")");
    }

    private static int EnlargeTmpLabels(Canvas canvas)
    {
        if (!_tmpChecked)
        {
            _tmpChecked = true;
            _tmpTextType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
            if (_tmpTextType != null)
            {
                _tmpFontSize = _tmpTextType.GetProperty("fontSize");
                _tmpAutoSize = _tmpTextType.GetProperty("enableAutoSizing");
            }
        }
        if (_tmpTextType == null || _tmpFontSize == null)
            return 0;

        Component[] labels = canvas.GetComponentsInChildren(_tmpTextType, true);
        int changed = 0;
        for (int i = 0; i < labels.Length; i++)
        {
            Component label = labels[i];
            if (label == null || !ScaledLabels.Add(label.GetInstanceID()))
                continue;
            float size = _tmpFontSize.GetValue(label, null) is float value ? value : 14f;
            float next = Mathf.Clamp(size > 1f ? size * CardTextScale : 14f * CardTextScale, 28f, 44f);
            if (size < next)
                _tmpFontSize.SetValue(label, next, null);
            if (_tmpAutoSize != null && _tmpAutoSize.PropertyType == typeof(bool))
                _tmpAutoSize.SetValue(label, false, null);
            changed++;
        }
        return changed;
    }
}

internal static class KksFolderPickerText
{
    private static bool _active;
    private static bool _fontsScaled;
    private static bool _logged;
    private static GUISkin _skin;
    private static readonly GUIStyle[] Styles = new GUIStyle[7];
    private static readonly int[] SavedSizes = new int[7];

    internal static void Begin()
    {
        End();
        _active = true;
    }

    internal static void End()
    {
        if (_fontsScaled && _skin != null)
        {
            for (int i = 0; i < Styles.Length; i++)
            {
                if (Styles[i] != null)
                    Styles[i].fontSize = SavedSizes[i];
                Styles[i] = null;
            }
        }
        _fontsScaled = false;
        _skin = null;
        _active = false;
    }

    internal static void Widen(ref Rect clientRect)
    {
        if (!_active)
            return;
        float screenW = Screen.width;
        float screenH = Screen.height;
        if (screenW < 16f || screenH < 16f)
            return;
        if (clientRect.x <= 1f
            && clientRect.height >= screenH * 0.75f
            && clientRect.width <= screenW * 0.18f)
        {
            clientRect.width = screenW * 0.26f;
        }
        ApplyFontScale(GUI.skin, clientRect.width);
    }

    private static void ApplyFontScale(GUISkin skin, float columnWidth)
    {
        if (skin == null || _fontsScaled)
            return;
        GUIStyle[] styles =
        {
            skin.window, skin.label, skin.button, skin.textField,
            skin.textArea, skin.toggle, skin.box
        };
        int applied = 0;
        for (int i = 0; i < styles.Length; i++)
        {
            GUIStyle style = styles[i];
            Styles[i] = style;
            if (style == null)
                continue;
            bool duplicate = false;
            for (int j = 0; j < i; j++)
            {
                if (Styles[j] == style)
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
            {
                Styles[i] = null;
                continue;
            }
            SavedSizes[i] = style.fontSize;
            int basis = style.fontSize > 0 ? style.fontSize : 14;
            style.fontSize = Mathf.Clamp(Mathf.RoundToInt(basis * 2.4f), 28, 42);
            applied = style.fontSize;
        }
        _fontsScaled = true;
        _skin = skin;
        if (!_logged && applied > 0)
        {
            _logged = true;
            VRLog.Info("Scene folder picker text set to " + applied
                + "px, column " + Mathf.RoundToInt(columnWidth) + "px.");
        }
    }
}

internal sealed class KksCanvasCaptureDriver : MonoBehaviour
{
    internal static KksCanvasCaptureDriver Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        KksCanvasCapture.LateMaintain();
    }
}

internal sealed class KksSortedRaycaster : GraphicRaycaster
{
    private Canvas owner;
    private int Order => (owner != null ? owner : owner = GetComponent<Canvas>()).sortingOrder;
    public override int sortOrderPriority => Order;
    public override int renderOrderPriority => Order;
}
