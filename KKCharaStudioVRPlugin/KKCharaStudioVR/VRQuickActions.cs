using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRGIN.Core;
using VRGIN.Visuals;
using Valve.VR;
using Studio;

namespace KKCharaStudioVR;

public class VRQuickActions : MonoBehaviour
{
    private const float GuiTogglePressMaxDuration = 0.45f;

    public static VRQuickActions Instance { get; private set; }

    private bool uiVisible = true;
    private Dictionary<GUIQuad, bool> previousStates = new Dictionary<GUIQuad, bool>();
    private float lastToggleTime = 0f;
    private Dictionary<GUIQuad, Vector3> originalScales = new Dictionary<GUIQuad, Vector3>();
    private Dictionary<GUIQuad, Coroutine> scaleCoroutines = new Dictionary<GUIQuad, Coroutine>();
    private bool _guiTogglePressActive;
    private bool _guiTogglePressChorded;
    private float _guiTogglePressStarted;
    private bool _presentationSuppressed;
    private readonly Dictionary<GUIQuad, bool> _presentationStates = new Dictionary<GUIQuad, bool>();
    private string _leftDeviceState;
    private string _rightDeviceState;
    private string _gateState;

    private void Awake()
    {
        Instance = this;
        PullIkGuideVisible();
    }

    private void OnDestroy()
    {
        SetPresentationSuppressed(false);
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (VR.Mode == null)
        {
            LogState(ref _gateState, "mode", "Menu input paused: VR.Mode is null");
            return;
        }

        string leftReason;
        string rightReason;
        SteamVR_Controller.Device leftController = GetDevice(VR.Mode.Left, out leftReason);
        SteamVR_Controller.Device rightController = GetDevice(VR.Mode.Right, out rightReason);
        LogDeviceState(ref _leftDeviceState, "left", leftController, leftReason);
        LogDeviceState(ref _rightDeviceState, "right", rightController, rightReason);

        KKCharaStudioVRSettings settings = GetSettings();
        // Playback pause must run before the MMD gate. While a clip is playing
        // the gate used to return first, so the stick click never reached it.
        TryToggleMmdFromLeftStick(leftController);
        string gate = CurrentInputGate();
        if (gate != null)
        {
            _guiTogglePressActive = false;
            LogState(ref _gateState, gate, "Menu input blocked: " + gate);
            LogBlockedToggleAttempts(settings, leftController, rightController, gate);
            if (_presentationSuppressed)
                SuppressNewPresentationGuiQuads();
            return;
        }

        if (_gateState != null)
        {
            _gateState = null;
            VRLog.Info("Menu input unblocked");
        }

        // GUI 显隐按键可使用当前双手布局，或集中到左手 X/Y、右手 A/B。
        HandleGuiToggleButton(settings, leftController, rightController);

        // 左摇杆按下：普通点击只切换 MMD 播放；Grip 组合才召唤主 GUI。
        if (leftController != null && leftController.GetPressDown(EVRButtonId.k_EButton_Axis0))
        {
            bool isGripPressed = leftController.GetPress(EVRButtonId.k_EButton_Grip);
            bool isTriggerPressed = leftController.GetPress(EVRButtonId.k_EButton_Axis1);
            bool isMenuPressed = leftController.GetPress(EVRButtonId.k_EButton_ApplicationMenu);

            if (isGripPressed && isTriggerPressed)
                ToggleReShade();
            else if (isGripPressed && isMenuPressed)
                NextReShadePreset();
            else if (isGripPressed)
                SummonMainGUI();
            else if (isTriggerPressed || isMenuPressed)
            {
                VRLog.Info("Left stick click skipped: chorded with "
                    + (isTriggerPressed ? "trigger " : "")
                    + (isMenuPressed ? "menu" : ""));
            }
        }

        // 右摇杆按下：Grip 组合召唤角色至眼前；非组合时优先控制 Timeline 或撤销。
        if (rightController != null && rightController.GetPressDown(EVRButtonId.k_EButton_Axis0))
        {
            bool isGripPressed = rightController.GetPress(EVRButtonId.k_EButton_Grip);
            bool isTriggerPressed = rightController.GetPress(EVRButtonId.k_EButton_Axis1);
            bool isMenuPressed = rightController.GetPress(EVRButtonId.k_EButton_ApplicationMenu);

            if (isGripPressed && isTriggerPressed)
            {
                string feedback;
                VRSpawnPlacementHelper.CallAllCharacters(false, out feedback);
                VRLog.Info("Right stick chord (Grip+Trigger): " + feedback);
                return;
            }
            if (isGripPressed)
            {
                string feedback;
                VRSpawnPlacementHelper.CallSelectedCharacter(false, out feedback);
                VRLog.Info("Right stick chord (Grip): " + feedback);
                return;
            }

            bool isChorded = isTriggerPressed || isMenuPressed;
            if (!isChorded)
            {
                // Play/pause owns an unchorded stick click. Do not undo, and do
                // not let the click fall through into a view change.
                if (VRMmdPlaybackController.TryHandleLeftStickPlaybackToggle())
                {
                    VRLog.Info("Right stick click: MMD playback toggle");
                    return;
                }
                if (VRTimelineCameraFollowController.TryHandleRightStickPlaybackToggle())
                {
                    VRLog.Info("Right stick click: timeline playback toggle");
                    return;
                }
                if (VRTimelineCameraFollowController.ShouldClaimRightStickTransport)
                {
                    VRLog.Info("Right stick click skipped: timeline transport owns the stick");
                    return;
                }
            }
            TryUndo();
        }
    }

    private int _ikVisibilityTicks;

    private void LateUpdate()
    {
        // Unity 2019 leaves Physics.autoSyncTransforms off. GUIQuad is a kinematic
        // rigidbody, so a transform move is written back to the old pose on the
        // next physics step unless we commit it. That pulled the studio panel
        // off the spot in front of the head.
        CommitGuiQuadPoses();

        // The IK guide switch lives in the settings object too (config panel,
        // wrist menu). Pick up an external change instead of waiting for the
        // wrist menu page to refresh, so both stay in sync.
        bool previousIkVisible = ikVisible;
        PullIkGuideVisible();
        bool ikChanged = previousIkVisible != ikVisible;

        // New guides spawn with renderers on, so the periodic sweep (a scene-wide
        // FindObjectsOfType) is only needed while guides are hidden.
        if (ikChanged || _ikVisibilityTicks < 8 || (!ikVisible && (Time.frameCount % 45) == 0))
        {
            _ikVisibilityTicks++;
            ApplyIkGuideVisibility();
        }
    }

    private static void CommitGuiQuadPoses()
    {
        bool any = false;
        foreach (GUIQuad quad in GUIQuadRegistry.Quads)
        {
            if (quad == null)
                continue;
            any = true;
            Rigidbody body = quad.GetComponent<Rigidbody>();
            if (body == null)
                continue;
            Transform quadTransform = quad.transform;
            body.position = quadTransform.position;
            body.rotation = quadTransform.rotation;
        }

        if (any)
            Physics.SyncTransforms();
    }

    private static bool _lastLeftStickHeld;

    private static bool TryToggleMmdFromLeftStick(SteamVR_Controller.Device leftController)
    {
        if (leftController == null)
        {
            _lastLeftStickHeld = false;
            return false;
        }

        bool held = leftController.GetPress(EVRButtonId.k_EButton_Axis0);
        bool down = leftController.GetPressDown(EVRButtonId.k_EButton_Axis0)
            || (!_lastLeftStickHeld && held);
        _lastLeftStickHeld = held;

        if (!down)
            return false;
        if (leftController.GetPress(EVRButtonId.k_EButton_Grip)
            || leftController.GetPress(EVRButtonId.k_EButton_Axis1)
            || leftController.GetPress(EVRButtonId.k_EButton_ApplicationMenu))
            return false;

        bool handled = VRMmdPlaybackController.TryHandleLeftStickPlaybackToggle();
        VRLog.Info(handled
            ? "Left stick click: MMD playback toggle"
            : "Left stick click skipped: MMD playback did not handle it");
        return true;
    }

    private string CurrentInputGate()
    {
        if (_presentationSuppressed)
            return "presentation suppressed";
        if (VRMmdPlaybackController.ConsumedPlaybackClickThisFrame)
            return "MMD consumed playback click";
        if (VRTimelineCameraFollowController.ConsumedRightStickTransportThisFrame)
            return "timeline consumed right stick";
        if (VRMmdPlaybackController.BlocksNormalInput)
            return "MMD blocks normal input";
        return null;
    }

    private void LogBlockedToggleAttempts(
        KKCharaStudioVRSettings settings,
        SteamVR_Controller.Device leftController,
        SteamVR_Controller.Device rightController,
        string gate)
    {
        SteamVR_Controller.Device guiDevice;
        EVRButtonId guiButton;
        string guiLabel;
        ResolveGuiToggleBinding(settings, leftController, rightController, out guiDevice, out guiButton, out guiLabel);
        if (guiDevice != null && guiDevice.GetPressDown(guiButton))
            VRLog.Info("GUI toggle skipped: " + gate + " (" + guiLabel + ")");

        if (leftController != null && leftController.GetPressDown(EVRButtonId.k_EButton_Axis0))
            VRLog.Info("Left stick click skipped: " + gate);
        if (rightController != null && rightController.GetPressDown(EVRButtonId.k_EButton_Axis0))
            VRLog.Info("Right stick click skipped: " + gate);
    }

    private static void LogDeviceState(ref string slot, string hand, SteamVR_Controller.Device device, string reason)
    {
        string key = device != null ? "ok" : (reason ?? "unavailable");
        LogState(
            ref slot,
            key,
            device != null
                ? "Menu input: " + hand + " controller ready"
                : "Menu input: " + hand + " controller unavailable (" + key + ")");
    }

    private static void LogState(ref string slot, string key, string message)
    {
        if (slot == key)
            return;
        slot = key;
        VRLog.Info(message);
    }

    private SteamVR_Controller.Device GetDevice(VRGIN.Controls.Controller controller, out string reason)
    {
        if (controller == null)
        {
            reason = "controller missing";
            return null;
        }

        try
        {
#if KKS
            // Device index can stay unpublished while the pose action already
            // reports button edges. ForController falls back to that pose.
            // Do not require pose.isValid: OpenXR drops it while buttons still update.
            SteamVR_Controller.Device device = SteamVR_Controller.ForController(controller);
#else
            int index = VRGameCompatibility.DeviceIndex(controller);
            if (index < 0)
            {
                reason = "no tracked device index";
                return null;
            }

            SteamVR_Controller.Device device = SteamVR_Controller.Input(index);
#endif
            if (device == null)
            {
                reason = "ForController returned null";
                return null;
            }
            if (!device.connected)
            {
                reason = "device disconnected";
                return null;
            }

            reason = null;
            return device;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            VRLog.Warn("Unable to read controller for quick actions: " + ex.Message);
            return null;
        }
    }

    internal void SetPresentationSuppressed(bool suppressed)
    {
        if (_presentationSuppressed == suppressed)
            return;
        _presentationSuppressed = suppressed;

        _guiTogglePressActive = false;
        if (suppressed)
        {
            _presentationStates.Clear();
            SuppressNewPresentationGuiQuads();
            return;
        }

        foreach (KeyValuePair<GUIQuad, bool> pair in new Dictionary<GUIQuad, bool>(_presentationStates))
        {
            GUIQuad quad = pair.Key;
            if (quad != null && quad.gameObject != null)
                quad.gameObject.SetActive(pair.Value);
        }
        _presentationStates.Clear();
    }

    private readonly List<GUIQuad> _presentationScratch = new List<GUIQuad>(8);

    private void SuppressNewPresentationGuiQuads()
    {
        // Runs every frame while MMD presentation is active. SetActive(false)
        // unregisters a quad from the registry mid-iteration, so a snapshot is
        // required, but it must reuse one list instead of allocating per frame.
        _presentationScratch.Clear();
        foreach (GUIQuad registered in GUIQuadRegistry.Quads)
            _presentationScratch.Add(registered);
        for (int i = 0; i < _presentationScratch.Count; i++)
        {
            GUIQuad quad = _presentationScratch[i];
            if (quad == null || quad.gameObject == null)
                continue;
            if (!_presentationStates.ContainsKey(quad))
                _presentationStates[quad] = quad.gameObject.activeSelf;
            if (quad.gameObject.activeSelf)
                quad.gameObject.SetActive(false);
        }
        _presentationScratch.Clear();
    }

    private static KKCharaStudioVRSettings GetSettings()
    {
        if (VR.Manager == null || VR.Manager.Context == null)
            return null;
        return VR.Manager.Context.Settings as KKCharaStudioVRSettings;
    }

    private static void ResolveGuiToggleBinding(
        KKCharaStudioVRSettings settings,
        SteamVR_Controller.Device leftController,
        SteamVR_Controller.Device rightController,
        out SteamVR_Controller.Device device,
        out EVRButtonId button,
        out string label)
    {
        string layout = settings != null
            ? settings.ControllerFaceButtonLayout
            : KKCharaStudioVRSettings.ControllerLayoutSplitHands;
        if (layout == KKCharaStudioVRSettings.ControllerLayoutLeftHand)
        {
            device = leftController;
            button = EVRButtonId.k_EButton_ApplicationMenu;
            label = "left Y/ApplicationMenu";
        }
        else if (layout == KKCharaStudioVRSettings.ControllerLayoutRightHand)
        {
            device = rightController;
            button = EVRButtonId.k_EButton_ApplicationMenu;
            label = "right B/ApplicationMenu";
        }
        else
        {
            // Split hands: right A shows/hides the Studio GUI. Left X is the wrist menu.
            device = rightController;
            button = EVRButtonId.k_EButton_A;
            label = "right A";
        }
    }

    private void HandleGuiToggleButton(
        KKCharaStudioVRSettings settings,
        SteamVR_Controller.Device leftController,
        SteamVR_Controller.Device rightController)
    {
        SteamVR_Controller.Device device;
        EVRButtonId button;
        string label;
        ResolveGuiToggleBinding(settings, leftController, rightController, out device, out button, out label);
        if (device == null)
        {
            _guiTogglePressActive = false;
            return;
        }

        if (device.GetPressDown(button))
        {
            _guiTogglePressActive = true;
            _guiTogglePressChorded = false;
            _guiTogglePressStarted = Time.unscaledTime;
        }

        if (_guiTogglePressActive)
        {
            // ApplicationMenu is also the long-hold reset. Grip/trigger chords
            // belong to grab and scale. Only an unchorded short release toggles.
            _guiTogglePressChorded |= device.GetPress(EVRButtonId.k_EButton_Grip)
                || device.GetPress(EVRButtonId.k_EButton_Axis1);
        }

        // OpenXR can drop the press-up edge. A tracked press that is no longer
        // held is the same short-press release KK handles with GetPressUp.
        bool released = _guiTogglePressActive
            && (device.GetPressUp(button) || !device.GetPress(button));
        if (!released)
            return;

        float duration = Time.unscaledTime - _guiTogglePressStarted;
        bool chorded = _guiTogglePressChorded;
        _guiTogglePressActive = false;
        if (chorded)
        {
            VRLog.Info("GUI toggle skipped: grip/trigger chord (" + label + ")");
            return;
        }
        if (duration > GuiTogglePressMaxDuration)
        {
            VRLog.Info("GUI toggle skipped: held " + duration.ToString("0.00") + "s (" + label + ")");
            return;
        }

        ToggleAllGUI();
    }

    private static Transform HeadsetHead()
    {
        try
        {
            if (VR.Camera == null)
                return null;
            Transform head = VR.Camera.Head;
            if (head != null)
                return head;
            if (VR.Camera.SteamCam != null)
                return ((Component)VR.Camera.SteamCam).transform;
            return ((Component)VR.Camera).transform;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<GUIQuad> CollectGuiQuads()
    {
        var quads = new List<GUIQuad>();
        var seen = new HashSet<GUIQuad>();
        foreach (GUIQuad quad in GUIQuadRegistry.Quads)
        {
            if (quad != null && seen.Add(quad))
                quads.Add(quad);
        }

        // OnDisable unregisters a quad. A quad that never registered (source was
        // not VR.GUI during Awake) would otherwise be invisible to the toggle.
        foreach (GUIQuad quad in UnityEngine.Object.FindObjectsOfType<GUIQuad>())
        {
            if (quad != null && seen.Add(quad))
                quads.Add(quad);
        }
        return quads;
    }

    private bool ToggleAllGUI(bool bypassDebounce = false)
    {
        if (!bypassDebounce && Time.time - lastToggleTime < 0.5f)
        {
            VRLog.Info("GUI toggle skipped: debounce");
            return false;
        }
        lastToggleTime = Time.time;

        uiVisible = !uiVisible;

        if (!uiVisible)
        {
            // HIDE: snapshot registry to avoid modification during iteration
            // (SetActive(false) in coroutine triggers OnDisable → Unregister)
            var currentQuads = CollectGuiQuads();
            int hidden = 0;
            foreach (var quad in currentQuads)
            {
                if (quad == null || quad.gameObject == null) continue;

                if (!originalScales.ContainsKey(quad) && quad.transform.localScale != Vector3.zero)
                {
                    originalScales[quad] = quad.transform.localScale;
                }

                Vector3 targetScale = originalScales.ContainsKey(quad) ? originalScales[quad] : Vector3.one;

                if (scaleCoroutines.ContainsKey(quad) && scaleCoroutines[quad] != null)
                {
                    StopCoroutine(scaleCoroutines[quad]);
                }

                previousStates[quad] = quad.gameObject.activeSelf;
                if (quad.gameObject.activeSelf)
                {
                    hidden++;
                    scaleCoroutines[quad] = StartCoroutine(ScaleAnimation(quad, Vector3.zero, true, targetScale));
                }
            }
            VRLog.Info("GUI toggle result: hidden, quads=" + currentQuads.Count + ", animating=" + hidden);
        }
        else
        {
            // SHOW: iterate previousStates — quads have been unregistered from
            // GUIQuadRegistry when SetActive(false) triggered OnDisable → Unregister,
            // so GUIQuadRegistry.Quads is empty. We must use our saved references.
            Transform head = HeadsetHead();
            int restored = 0;
            KKCharaStudioVRSettings settings = GetSettings();
            float guiDistance = settings != null ? settings.UISpawnDistance : KKCharaStudioVRSettings.DefaultUISpawnDistance;

            foreach (var kvp in new Dictionary<GUIQuad, bool>(previousStates))
            {
                var quad = kvp.Key;
                bool wasActive = kvp.Value;
                if (quad == null || quad.gameObject == null) continue;
                if (!wasActive) continue;

                if (!originalScales.ContainsKey(quad) && quad.transform.localScale != Vector3.zero)
                {
                    originalScales[quad] = quad.transform.localScale;
                }
                Vector3 targetScale = originalScales.ContainsKey(quad) ? originalScales[quad] : Vector3.one;

                // Reposition naturally in front of head (perpendicular to user's gaze)
                if (head != null)
                {
                    Vector3 forward = head.forward;
                    if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                    forward.Normalize();

                    quad.transform.position = head.position + forward * guiDistance;
                    quad.transform.rotation = Quaternion.LookRotation(forward, head.up);
                }

                if (scaleCoroutines.ContainsKey(quad) && scaleCoroutines[quad] != null)
                {
                    StopCoroutine(scaleCoroutines[quad]);
                }

                quad.gameObject.SetActive(true);
                quad.transform.localScale = Vector3.zero;
                scaleCoroutines[quad] = StartCoroutine(ScaleAnimation(quad, targetScale, false, targetScale));
                restored++;
            }
            previousStates.Clear();
            VRLog.Info("GUI toggle result: visible, restored=" + restored
                + (head != null ? ", respawned in front of head" : ", skipped respawn because head is null"));
        }
        return true;
    }

    private IEnumerator ScaleAnimation(GUIQuad quad, Vector3 targetScale, bool hideAfter, Vector3 restoreScale)
    {
        Vector3 startScale = quad.transform.localScale;
        float elapsed = 0f;
        float duration = 0.2f;

        while (elapsed < duration)
        {
            if (quad == null || quad.gameObject == null) yield break;
            elapsed += Time.deltaTime;
            quad.transform.localScale = Vector3.Lerp(startScale, targetScale, elapsed / duration);
            yield return null;
        }

        if (quad != null && quad.gameObject != null)
        {
            quad.transform.localScale = targetScale;
            if (hideAfter)
            {
                quad.gameObject.SetActive(false);
                quad.transform.localScale = restoreScale;
            }
        }
    }

    public void ForceHideUI()
    {
        if (uiVisible)
        {
            ToggleAllGUI();
        }
    }

    public bool SummonMainGUI()
    {
        return PlaceMainGUI(true);
    }

    public void RepositionMainGUIWithoutChangingVisibility()
    {
        PlaceMainGUI(false);
    }

    internal void RememberMainGUIScale(GUIQuad mainQuad)
    {
        if (mainQuad != null && mainQuad.gameObject != null
            && mainQuad.transform.localScale != Vector3.zero)
        {
            originalScales[mainQuad] = mainQuad.transform.localScale;
        }
    }

    private bool PlaceMainGUI(bool reveal)
    {
        // Search both registry (active quads) and previousStates (hidden quads)
        GUIQuad mainQuad = VRCameraMoveHelper.GetMainUI();
        float maxSize = mainQuad != null ? float.MaxValue : -1f;

        foreach (var quad in GUIQuadRegistry.Quads)
        {
            if (quad == null || quad.gameObject == null) continue;
            float size = quad.transform.localScale.x * quad.transform.localScale.y;
            if (size > maxSize)
            {
                maxSize = size;
                mainQuad = quad;
            }
        }

        // Also check hidden quads saved in previousStates
        foreach (var kvp in previousStates)
        {
            var quad = kvp.Key;
            if (quad == null || quad.gameObject == null) continue;
            if (!kvp.Value) continue; // was not active
            // Use originalScales for size since hidden quads have scale=0
            Vector3 s = originalScales.ContainsKey(quad) ? originalScales[quad] : Vector3.one;
            float size = s.x * s.y;
            if (size > maxSize)
            {
                maxSize = size;
                mainQuad = quad;
            }
        }

        if (mainQuad == null)
        {
            VRLog.Info("Summon main GUI skipped: no GUIQuad in the registry or saved set");
            return false;
        }

        Transform head = HeadsetHead();
        if (head == null)
        {
            VRLog.Info("Summon main GUI skipped: VR.Camera.Head is null");
            return false;
        }

        KKCharaStudioVRSettings settings = GetSettings();
        float guiDistance = settings != null ? settings.UISpawnDistance : KKCharaStudioVRSettings.DefaultUISpawnDistance;
        float guiScale = settings != null ? settings.UISpawnScale : KKCharaStudioVRSettings.DefaultUISpawnScale;

                Vector3 forward = head.forward;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                forward.Normalize();

                mainQuad.transform.position = head.position + forward * guiDistance;
                mainQuad.transform.rotation = Quaternion.LookRotation(forward, head.up);
                VRLog.Info(reveal
                    ? "Summon main GUI result: placed in front of head"
                    : "Main GUI placement updated without changing visibility");

                Vector3 targetScale = VRCameraMoveHelper.GetMainUIScale(guiScale);
                originalScales[mainQuad] = targetScale;

                if (!uiVisible)
                {
                    mainQuad.transform.localScale = targetScale;
                    if (reveal)
                    {
                        // Explicit Recall reveals the prior UI set; +/- previews do not.
                        if (!ToggleAllGUI(true))
                            return false;
                        mainQuad.UpdateGUI();
                    }
                }
                else
                {
                if (reveal)
                {
                    mainQuad.gameObject.SetActive(true);
                    mainQuad.UpdateGUI();
                }

                    if (!mainQuad.gameObject.activeSelf)
                    {
                        mainQuad.transform.localScale = targetScale;
                        VRLog.Info("Summon main GUI skipped: main quad stayed inactive");
                        return false;
                    }

                    if (scaleCoroutines.ContainsKey(mainQuad) && scaleCoroutines[mainQuad] != null)
                    {
                        StopCoroutine(scaleCoroutines[mainQuad]);
                    }
                    mainQuad.transform.localScale = Vector3.zero;
                    scaleCoroutines[mainQuad] = StartCoroutine(ScaleAnimation(mainQuad, targetScale, false, targetScale));
                }
        return !reveal || (uiVisible && mainQuad.gameObject.activeSelf);
    }

    private void TryUndo()
    {
        try
        {
            if (Singleton<Studio.Studio>.Instance != null)
            {
                Singleton<UndoRedoManager>.Instance.Undo();
                VRLog.Info("Undo executed");
            }
            else
            {
                VRLog.Info("Undo skipped: Studio instance is null");
            }
        }
        catch (Exception e)
        {
            VRLog.Warn("Undo failed: " + e.Message);
        }
    }

    public static bool ikVisible = true;

    internal static void PullIkGuideVisible()
    {
        if (VRInteractionOptions.Settings != null)
            ikVisible = VRInteractionOptions.IkGuideVisible;
    }

    internal static void SetIkGuideVisible(bool visible)
    {
        ikVisible = visible;
        KKCharaStudioVRSettings settings = VRInteractionOptions.Settings;
        if (settings != null && settings.IkGuideVisible != visible)
            settings.IkGuideVisible = visible;
        ApplyIkGuideVisibility();
    }

    private void ToggleIKVisibility()
    {
        PullIkGuideVisible();
        SetIkGuideVisible(!ikVisible);
        VRLog.Info("Toggled IK guide renderers to: " + ikVisible + " (colliders stay enabled)");
    }

    internal static void ApplyIkGuideVisibility()
    {
        bool show = VRStudioInteractionPolicy.IkRendererEnabled(ikVisible);
        bool colliders = VRStudioInteractionPolicy.IkColliderEnabled(ikVisible);

        // Studio owns the green spheres through GuideObject.visible. Flipping only
        // the renderers the plugin created left the native spheres on screen.
        ApplyNativeGuideVisibility(VRStudioInteractionPolicy.NativeGuideVisible(ikVisible));

        MoveableGUIObject[] mgos = UnityEngine.Object.FindObjectsOfType<MoveableGUIObject>();
        foreach (MoveableGUIObject mgo in mgos)
        {
            if (mgo == null || mgo.gameObject == null || mgo.guideObject == null)
                continue;
            Renderer[] renderers = mgo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = show;
            }
            Collider[] cols = mgo.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = colliders;
            }
        }

        if (Singleton<GuideObjectManager>.Instance == null || GuideDictionaryField == null)
            return;
        object raw = GuideDictionaryField.GetValue(Singleton<GuideObjectManager>.Instance);
        if (raw is Dictionary<Transform, GuideObject> typed)
        {
            foreach (KeyValuePair<Transform, GuideObject> pair in typed)
                ApplyGuideVisual(pair.Value, show, colliders);
        }
        else if (raw is System.Collections.IDictionary dic)
        {
            foreach (System.Collections.DictionaryEntry entry in dic)
                ApplyGuideVisual(entry.Value as GuideObject, show, colliders);
        }
    }

    private static bool _nativeGuideFailureLogged;

    private static void ApplyNativeGuideVisibility(bool visible)
    {
        Studio.Studio studio = Singleton<Studio.Studio>.Instance;
        if (studio == null || studio.dicObjectCtrl == null)
            return;
        try
        {
            foreach (KeyValuePair<int, ObjectCtrlInfo> pair in studio.dicObjectCtrl)
            {
                OCIChar character = pair.Value as OCIChar;
                if (character == null)
                    continue;
                character.VisibleIKGuide(visible);
                character.VisibleFKGuide(visible);
            }
        }
        catch (Exception ex)
        {
            if (_nativeGuideFailureLogged)
                return;
            _nativeGuideFailureLogged = true;
            VRLog.Warn("Native IK guide visibility call failed: " + ex.Message);
        }
    }

    private static readonly System.Reflection.FieldInfo GuideDictionaryField = typeof(GuideObjectManager).GetField(
        "dicGuideObject",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    private static void ApplyGuideVisual(GuideObject guide, bool show, bool colliders)
    {
        if (guide == null)
            return;
        // Sweep the whole guide, not just the axis gizmos: the green ball is the
        // "Sphere" child, and Studio's own draw flag turns its collider off with it.
        // Studio keeps an unselected guide's gizmo deactivated on purpose, so the
        // GameObjects are never reactivated here.
        bool guideShow = VRStudioInteractionPolicy.GuideRendererEnabled(show, guide.visible, guide.visibleOutside);
        Renderer[] renderers = guide.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = guideShow;
        }
        Collider[] cols = guide.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = colliders;
        }
    }

    public bool ToggleIkControls(out string status)
    {
        ToggleIKVisibility();
        status = ikVisible ? "IK 控制器已显示" : "IK 控制器已隐藏";
        return true;
    }

    private void ToggleReShade()
    {
        VRLog.Info("Left controller Left Joystick Click + Grip + Trigger pressed! Toggling ReShade (End key)...");
        KeyboradSimulatorUtil.PressEndKey();
    }

    private void NextReShadePreset()
    {
        VRLog.Info("Left controller Left Joystick Click + Grip + Menu pressed! Cycling ReShade preset (PageDown key)...");
        KeyboradSimulatorUtil.PressPageDown();
    }

    public static void CycleNextReShadePreset()
    {
        KeyboradSimulatorUtil.PressPageDown();
    }

    public static void CyclePrevReShadePreset()
    {
        KeyboradSimulatorUtil.PressPageUp();
    }
}
