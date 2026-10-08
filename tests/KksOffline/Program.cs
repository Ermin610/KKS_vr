using System.Text.Json;
using KKCharaStudioVR;
using KK_VR_CameraSync;
using UnityEngine;
using Valve.VR;

static partial class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static void Pose(Transform origin, Vector3 position, Quaternion rotation, string message)
    {
        Check(Vector3.Distance(origin.position, position) < .002f && Quaternion.Angle(origin.rotation, rotation) < .1f,
            message + ": position=" + origin.position);
    }
    public static void Main()
    {
        Mapping();
        Input();
        BackgroundPresetPolicyChecks();
        Timeline();
        CardGridAndLruCache();
        ThumbnailDiskCacheAndBrowserLayout();
        MirrorObliquePolicy();
        MirrorStereoPasses();
        CameraTrim();
        MmdDanceRatingsAndCategories();
        StickTransportAndIkInteraction();
        ReviewRegressions();
        ClothingPresetHitTest();
        MmdFreeLocomotion();
        FigureScale();
        MmdPoseSanitize();
        Console.WriteLine($"PASS: {checks} offline assertions against linked production input/Timeline/card grid/MMD trim/ratings code.");
        Console.WriteLine("Unity math and SteamVR/game services are doubles; no headset, game process or installation was used.");
    }
    private static void Mapping()
    {
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/a", "click") == "/actions/legacy_emulate/in/a_press", "Right A is k_EButton_A");
        Check(SteamVR_Controller.OculusOutput("/user/hand/left/input/x", "click") == "/actions/legacy_emulate/in/a_press", "Left X is k_EButton_A");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/a", "touch") == "/actions/legacy_emulate/in/a_touch", "Right A touch");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/b", "click") == "/actions/legacy_emulate/in/applicationmenu_press", "Right B is ApplicationMenu");
        Check(SteamVR_Controller.OculusOutput("/user/hand/left/input/y", "click") == "/actions/legacy_emulate/in/applicationmenu_press", "Left Y is ApplicationMenu");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/b", "click") != SteamVR_Controller.OculusOutput("/user/hand/right/input/joystick", "click"), "B is not the stick click");
        Check(SteamVR_Controller.OculusOutput("/user/hand/left/input/joystick", "position") == "/actions/legacy_emulate/in/axis0_2d", "Left stick axes are Axis0");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/joystick", "position") == "/actions/legacy_emulate/in/axis0_2d", "Right stick axes are Axis0");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/joystick", "click") == "/actions/legacy_emulate/in/axis0_press", "Stick click is Axis0");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/trigger", "click") == "/actions/legacy_emulate/in/axis1_press", "Trigger click is Axis1");
        Check(SteamVR_Controller.OculusOutput("/user/hand/left/input/trigger", "pull") == "/actions/legacy_emulate/in/axis1_1d", "Trigger analog is Axis1_1D");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/grip", "click") == "/actions/legacy_emulate/in/grip_press", "Grip click");
        Check(SteamVR_Controller.OculusOutput("/user/hand/left/input/grip", "pull") == "/actions/legacy_emulate/in/grip_1d", "Grip analog");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/x", "click") == null, "Right X is not an oculus_touch path");
        Check(SteamVR_Controller.OculusOutput("/user/hand/right/input/y", "click") == null, "Right Y is not an oculus_touch path");
        string json = SteamVR_Controller.BuildOculusTouchBindingJson();
        using (JsonDocument.Parse(json)) { }
        Check(json.Contains("kks_legacy_oculus_touch") && json.Contains("/actions/legacy_emulate/out/huptic"), "Binding document name and haptics");
        foreach (var source in SteamVR_Controller.OculusBindings)
        {
            foreach (string slot in new[] { "click", "touch", "pull", "position" })
            {
                string output = SteamVR_Controller.OculusOutput(source.Path, slot);
                if (output != null)
                    Check(json.Contains(output) && json.Contains(source.Path), source.Path + " " + slot + " is in the binding file");
            }
        }
        Check(!json.Contains("/user/hand/right/input/x") && !json.Contains("/user/hand/right/input/y"), "Invalid Quest paths stay out of the binding");
        Check(SteamVR_Controller.SuppressSingleToolCycle(0) && SteamVR_Controller.SuppressSingleToolCycle(1) && !SteamVR_Controller.SuppressSingleToolCycle(2), "Single-tool mode does not cycle");
        Check(SteamVR_Controller.ResolveTrackedIndex(2, SteamVR_Input_Sources.LeftHand, false) == 2, "Published index wins");
        Check(SteamVR_Controller.ResolveTrackedIndex(-1, SteamVR_Input_Sources.LeftHand, false) == -1, "Inactive unpublished index is none");
        Check(SteamVR_Controller.ResolveTrackedIndex(-1, SteamVR_Input_Sources.LeftHand, true) == SteamVR_Controller.UnpublishedLeftIndex, "Left unpublished sentinel");
        Check(SteamVR_Controller.ResolveTrackedIndex(-1, SteamVR_Input_Sources.RightHand, true) == SteamVR_Controller.UnpublishedRightIndex, "Right unpublished sentinel");
        Check(SteamVR_Controller.ResolveTrackedIndex(-1, SteamVR_Input_Sources.Any, true) == -1, "Any source has no sentinel");
        Console.WriteLine("PASS: oculus_touch legacy button map and unpublished indices.");
    }
    private static void Input()
    {
        SteamVR_Controller.LogButtonEdges = false;
        var pose = new SteamVR_Behaviour_Pose { inputSource = SteamVR_Input_Sources.RightHand, index = 12 };
        var device = new SteamVR_Controller.Device(pose);
        var press = device.GetActionBoolean_Press(EVRButtonId.k_EButton_A);
        var touch = device.GetActionBoolean_Touch(EVRButtonId.k_EButton_A);
        ulong a = 1UL << (int)EVRButtonId.k_EButton_A;
        ulong grip = 1UL << (int)EVRButtonId.k_EButton_Grip;
        press.state = press.stateDown = true;
        Check(device.GetPressDown(a) && device.GetPress(a), "A button must be included in masks");
        Check(!device.GetTouch(a) && !device.GetTouchDown(a), "Press must not masquerade as touch");
        touch.state = touch.stateDown = true;
        Check(device.GetTouch(a) && device.GetTouchDown(a), "Touch-down must use touch action");
        touch.state = touch.stateDown = false;
        touch.stateUp = true;
        Check(device.GetTouchUp(EVRButtonId.k_EButton_A) && device.GetTouchUp(a), "Touch-up while press remains held");
        Check(!device.GetPressUp(a), "Held press must not release on touch-up");
        press.stateDown = false;
        var other = device.GetActionBoolean_Press(EVRButtonId.k_EButton_Grip);
        other.state = other.stateDown = true;
        Check(!device.GetPressDown(a | grip), "Composite mask already held must not press again");
        press.state = false;
        press.stateUp = true;
        Check(!device.GetPressUp(a | grip), "Composite mask waits until last button releases");
        other.state = other.stateDown = false;
        other.stateUp = true;
        Check(device.GetPressUp(a | grip), "Last mask button release");
        Check(!device.GetPress(0UL) && !device.GetTouchUp(0UL), "Empty mask stays inactive");
        SteamVR_Actions.legacy_emulate.Axis1_1D.axis = .8f;
        SteamVR_Actions.legacy_emulate.Grip_1D.axis = .6f;
        SteamVR_Actions.legacy_emulate.Axis0_2D.axis = new Vector2(.25f, -.5f);
        Check(device.GetAxis(EVRButtonId.k_EButton_Axis1).x == .8f, "Trigger uses scalar action");
        Check(device.GetAxis(EVRButtonId.k_EButton_SteamVR_Trigger).x == .8f, "Trigger alias uses scalar action");
        Check(device.GetAxis(EVRButtonId.k_EButton_Axis2).x == .6f, "Grip uses scalar action");
        Check(device.GetAxis().x == .25f && device.GetAxis().y == -.5f, "Stick uses Axis0_2D");
        Check(device.GetAxis(EVRButtonId.k_EButton_SteamVR_Touchpad).y == -.5f, "Touchpad alias is Axis0");
        pose.isValid = false;
        pose.isActive = false;
        press.state = press.stateDown = true;
        Check(device.connected && device.GetPress(EVRButtonId.k_EButton_A) && device.GetPressDown(EVRButtonId.k_EButton_A), "Pose valid/active flags must not block buttons");
        pose.isValid = true;
        pose.isActive = true;
        device.TriggerHapticPulse(1500);
        var haptic = SteamVR_Actions.legacy_emulate.Huptic;
        Check(haptic.calls == 1 && haptic.lastSource == SteamVR_Input_Sources.RightHand && Math.Abs(haptic.duration - .0015f) < .00001f, "Haptic duration and action source");
        pose.poseAction.source.deviceIsConnected = false;
        press.state = press.stateDown = true;
        Check(!device.GetPress(EVRButtonId.k_EButton_A) && !device.GetPressDown(a) && !device.GetTouchUp(a) && device.GetAxis().x == 0, "Disconnected cached actions cannot drive features");
        device.TriggerHapticPulse();
        Check(haptic.calls == 1 && !device.hasTracking, "Disconnected controller has no haptics/tracking");
        pose.poseAction = null;
        Check(!device.connected && device.uninitialized && !device.calibrating && !device.outOfRange && device.GetAxis().x == 0, "Missing pose action is safe");
        var absent = new SteamVR_Controller.Device(null);
        Check(!absent.GetPress(a) && !absent.GetTouch(EVRButtonId.k_EButton_A), "Destroyed pose is safe");
        pose.poseAction = new();
        VRGIN.Core.VR.Mode = new() { Right = new() { Tracking = pose } };
        Check(SteamVR_Controller.Input(12) != null && SteamVR_Controller.Input(99) == null && SteamVR_Controller.Input(-1) == null, "Physical device indices bind only current controllers");
        Check(ReferenceEquals(SteamVR_Controller.Input(12), SteamVR_Controller.Input(12)), "Device cache remains stable");
        VRGIN.Core.VR.Settings.Rumble = false;
        device.TriggerHapticPulse();
        Check(haptic.calls == 1, "Rumble preference is honored");
        var leftPose = new SteamVR_Behaviour_Pose { inputSource = SteamVR_Input_Sources.LeftHand, index = -1, isActive = false, isValid = false };
        var rightPose = new SteamVR_Behaviour_Pose { inputSource = SteamVR_Input_Sources.RightHand, index = -1 };
        VRGIN.Core.VR.Mode = new() { Left = new() { Tracking = leftPose }, Right = new() { Tracking = rightPose } };
        var leftDevice = SteamVR_Controller.Input(SteamVR_Controller.UnpublishedLeftIndex);
        var rightDevice = SteamVR_Controller.Input(SteamVR_Controller.UnpublishedRightIndex);
        Check(leftDevice != null && rightDevice != null && !ReferenceEquals(leftDevice, rightDevice), "Unpublished indices bind the matching hand");
        var leftA = leftDevice.GetActionBoolean_Press(EVRButtonId.k_EButton_A);
        leftA.state = leftA.stateDown = true;
        Check(leftDevice.GetPress(EVRButtonId.k_EButton_A) && leftDevice.GetPressDown(EVRButtonId.k_EButton_A), "Invalid pose still reports buttons while the device is connected");
        Check(!rightDevice.GetPress(EVRButtonId.k_EButton_A), "Left A does not leak to the right hand");
        SteamVR_Controller.Remember(3, leftPose);
        Check(SteamVR_Controller.Input(3).GetPress(EVRButtonId.k_EButton_A), "Remembered OpenVR index reaches the unpublished left pose");
        rightPose.index = 4;
        Check(SteamVR_Controller.Input(4) != null && SteamVR_Controller.Input(99) == null, "Published index replaces the sentinel");
        leftPose.inputSource = SteamVR_Input_Sources.Any;
        leftDevice.GetPress(EVRButtonId.k_EButton_Grip);
        Check(leftPose.inputSource == SteamVR_Input_Sources.LeftHand, "Any source on the left pose is corrected");
        var lines = new List<string>();
        SteamVR_Controller.EdgeLog = lines.Add;
        SteamVR_Controller.LogButtonEdges = true;
        var menu = rightDevice.GetActionBoolean_Press(EVRButtonId.k_EButton_ApplicationMenu);
        menu.stateUp = false;
        rightDevice.GetPressUp(EVRButtonId.k_EButton_ApplicationMenu);
        menu.stateUp = true;
        rightDevice.GetPressUp(EVRButtonId.k_EButton_ApplicationMenu);
        rightDevice.GetPressUp(EVRButtonId.k_EButton_ApplicationMenu);
        Check(lines.Count == 1 && lines[0] == "[KKS VR Input] R AppMenu up", "AppMenu up logged once per edge");
        menu.stateUp = false;
        rightDevice.GetPressUp(EVRButtonId.k_EButton_ApplicationMenu);
        menu.stateUp = true;
        rightDevice.GetPressUp(EVRButtonId.k_EButton_ApplicationMenu);
        Check(lines.Count == 2, "AppMenu up logs again after release");
        var gripPress = leftDevice.GetActionBoolean_Press(EVRButtonId.k_EButton_Grip);
        gripPress.stateDown = true;
        leftDevice.GetPressDown(EVRButtonId.k_EButton_Grip);
        Check(lines.Count == 3 && lines[2] == "[KKS VR Input] L Grip down", "Left grip down");
        var trigger = rightDevice.GetActionBoolean_Press(EVRButtonId.k_EButton_Axis1);
        trigger.stateDown = true;
        rightDevice.GetPressDown(EVRButtonId.k_EButton_SteamVR_Trigger);
        Check(lines.Count == 4 && lines[3] == "[KKS VR Input] R Trigger down", "Trigger alias logs as Trigger");
        var stick = leftDevice.GetActionBoolean_Press(EVRButtonId.k_EButton_Axis0);
        stick.stateUp = true;
        leftDevice.GetPressUp(EVRButtonId.k_EButton_SteamVR_Touchpad);
        Check(lines.Count == 5 && lines[4] == "[KKS VR Input] L StickClick up", "Touchpad alias logs as StickClick");
        stick.state = stick.stateDown = true;
        stick.stateUp = false;
        leftDevice.GetPressDown(EVRButtonId.k_EButton_Axis0);
        Check(lines.Count == 6 && lines[5] == "[KKS VR Input] L StickClick down", "Left stick click down logged");
        Console.WriteLine("PASS: input edges, masks, analog mapping, disconnects, device binding and haptics.");
    }
    private static void Timeline()
    {
        Check(CameraSyncDriver.TimelineDisabled, "Timeline is disabled by default");
        var disabledOrigin = new Transform { position = new(2, 1, -3), rotation = Quaternion.AngleAxis(35, Vector3.up) };
        var disabledDriver = new CameraSyncDriver { Target = new() { Position = new(4, 2, 1), Rotation = Quaternion.AngleAxis(70, Vector3.up) } };
        Check(!disabledDriver.IsTimelineCameraWriterActive(), "Disabled timeline writer is inactive");
        Check(!disabledDriver.Tick(disabledOrigin), "Disabled timeline does not claim tick");

        CameraSyncDriver.TimelineDisabled = false;
        try
        {
            Check(CameraSyncDriver.ValidateTimelinePoseMath(), "Existing pose invariants");
            var origin = new Transform { position = new(2, 1, -3), rotation = Quaternion.AngleAxis(35, Vector3.up) };
            var driver = new CameraSyncDriver { Target = new() { Position = new(4, 2, 1), Rotation = Quaternion.AngleAxis(70, Vector3.up) } };
            driver.Tick(origin);
        driver.SetTimelineUserPoseOffset(1.25f, 40);
        driver.Tick(origin);
        Vector3 expected = origin.position;
        Quaternion expectedRotation = origin.rotation;
        for (int i = 0; i < 100; i++)
        {
            Check(!driver.Tick(origin, false), "Stopped Timeline returns control to generic driver");
            driver.Tick(origin);
            Pose(origin, expected, expectedRotation, "Pause/resume must not accumulate offsets");
        }
        driver.SetTimelineCameraSuppressed(true);
        driver.Target.Position += new Vector3(5, 1, 3);
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "Animation-only mode must not move origin");
        Check(!driver.IsTimelineCameraWriterActive(), "Suppression releases writer ownership");
        driver.SetTimelineCameraSuppressed(false);
        driver.ResumeAndPrimeTimelineState();
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "Releasing suppression preserves no-align pose");
        driver.SetExternalVrCameraOwner(true);
        origin.position = new(-3, 4, 8);
        origin.rotation = Quaternion.AngleAxis(-80, Vector3.up);
        driver.Tick(origin);
        expected = origin.position; expectedRotation = origin.rotation;
        Check(!driver.IsTimelineCameraWriterActive(), "External owner releases writer ownership");
        driver.SetExternalVrCameraOwner(false);
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "External camera handoff must not double offsets");
        driver.Target.Source = CameraPoseSource.ObjectCamera;
        driver.Target.Rotation = Quaternion.AngleAxis(-50, Vector3.up);
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "Source change rebases without drift");
        driver.PoseAvailable = false;
        driver.Tick(origin);
        driver.PoseAvailable = true;
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "Missing camera and recovery preserve pose");
        driver.Tick(origin, false);
        driver.SetTimelineUserPoseOffset(2.25f, 40);
        driver.Tick(origin);
        Pose(origin, expected + Vector3.up, expectedRotation, "Offset edits while paused apply on resume");
        driver.Tick(origin, false);
        driver.ClearTimelineUserPoseOffset();
        driver.Tick(origin);
        expected = origin.position; expectedRotation = origin.rotation;
        driver.Tick(origin, false);
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "Cleared offsets do not return on another resume");
        origin = new Transform { position = new(9, 3, -5), rotation = Quaternion.AngleAxis(120, Vector3.up) };
        expected = origin.position; expectedRotation = origin.rotation;
        driver.Tick(origin);
        Pose(origin, expected, expectedRotation, "Replacement rig must not reuse old origin anchor");
        driver.Target.Position += new Vector3(1, 0, 0);
        driver.Tick(origin);
        Pose(origin, expected + new Vector3(1, 0, 0), expectedRotation, "Camera translation follows after rebasing");
        driver.SetTimelineUserPoseOffset(float.NaN, 0);
        driver.Tick(origin);
        Pose(origin, expected + new Vector3(1, 0, 0), expectedRotation, "Invalid offset cannot poison transform");
        CameraSyncDriver.RigAvailable = false;
        Check(!driver.IsTimelineCameraWriterActive(), "Missing rig cannot own camera");
        CameraSyncDriver.RigAvailable = true;
        CameraSyncDriver.Loading = true;
        Check(!driver.IsTimelineCameraWriterActive(), "Scene load releases ownership");
        CameraSyncDriver.Loading = false;
        var configured = new CameraSyncDriver();
        var firstOrigin = new Transform();
        configured.SetTimelineUserPoseOffset(2, 90);
        configured.Tick(firstOrigin);
        Pose(firstOrigin, Vector3.up * 2, Quaternion.AngleAxis(90, Vector3.up), "Offsets configured before first play must apply");
        Plugin.Instance.AlignOnTimelinePlay.Value = true;
        driver.ResetBaseline();
        driver.Tick(origin);
        Check(driver.SnapCalls == 1, "Aligned play captures neutral anchor once");
        for (int i = 0; i < 1000; i++) driver.Tick(origin);
        Check(driver.SnapCalls == 1, "Tracking must not be realigned every frame");
        driver.ClearTimelineUserPoseOffset();
        driver.Tick(origin);
        Pose(origin, driver.Target.Position, driver.Target.Rotation, "Clearing offsets restores authored pose with neutral head");

        Plugin.Instance.AlignOnTimelinePlay.Value = false;
        var slow = new CameraSyncDriver
        {
            Target = new CameraPose { Position = new(1, 2, 3), Rotation = Quaternion.identity }
        };
        var slowOrigin = new Transform { position = new(1, 2, 3), rotation = Quaternion.identity };
        slow.Tick(slowOrigin);
        // 0.05 mm is under the old 0.1 mm write gate and above the new hold.
        slow.Target.Position = new Vector3(1.00005f, 2, 3);
        slow.Tick(slowOrigin);
        Check(Vector3.Distance(slowOrigin.position, new Vector3(1, 2, 3)) > 0.00002f,
            "Slow Timeline translation below the old 0.1 mm hold is applied");

        Plugin.Instance.AlignOnTimelinePlay.Value = true;
        var aligned = new CameraSyncDriver
        {
            Target = new CameraPose { Position = new(3, 1, 0), Rotation = Quaternion.identity }
        };
        var alignedOrigin = new Transform();
        aligned.Tick(alignedOrigin);
        Check(aligned.SnapCalls == 1, "Align snaps once when playback acquires the camera");
        aligned.PoseAvailable = false;
        aligned.Tick(alignedOrigin);
        aligned.PoseAvailable = true;
        aligned.Tick(alignedOrigin);
        Check(aligned.SnapCalls == 1, "One missed camera sample does not snap the headset again");
        aligned.Target.Source = CameraPoseSource.ObjectCamera;
        aligned.Tick(alignedOrigin);
        Check(aligned.SnapCalls == 1, "A camera source change rebases without snapping");

        Plugin.Instance.AlignOnTimelinePlay.Value = false;
        var leased = new CameraSyncDriver
        {
            Target = new CameraPose { Position = new(1, 2, 3), Rotation = Quaternion.identity }
        };
        var leasedOrigin = new Transform { position = new(1, 2, 3), rotation = Quaternion.identity };
        leased.SetExternalVrCameraOwner(true);
        leased.Target.Position = new Vector3(8, 2, 3);
        for (int i = 0; i < CameraSyncDriver.ExternalOwnerLeaseTicks; i++)
            leased.Tick(leasedOrigin);
        Pose(leasedOrigin, new Vector3(1, 2, 3), Quaternion.identity, "An unreleased MMD lock holds for the lease window");
        leased.Tick(leasedOrigin);
        leased.Target.Position = new Vector3(9, 2, 3);
        leased.Tick(leasedOrigin);
        Pose(leasedOrigin, new Vector3(2, 2, 3), Quaternion.identity, "An expired MMD lock lets Timeline motion through");

        var renewed = new CameraSyncDriver
        {
            Target = new CameraPose { Position = new(1, 2, 3), Rotation = Quaternion.identity }
        };
        var renewedOrigin = new Transform { position = new(1, 2, 3), rotation = Quaternion.identity };
        for (int i = 0; i < CameraSyncDriver.ExternalOwnerLeaseTicks + 8; i++)
        {
            renewed.SetExternalVrCameraOwner(true);
            renewed.Target.Position = new Vector3(4 + i, 2, 3);
            renewed.Tick(renewedOrigin);
        }
        Pose(renewedOrigin, new Vector3(1, 2, 3), Quaternion.identity, "Renewing the MMD lock keeps Timeline from taking the camera");
        renewed.SetExternalVrCameraOwner(false);
        Check(renewed.IsTimelineCameraWriterActive(), "Unlocking MMD returns Timeline camera ownership");
        Console.WriteLine("PASS: Timeline pause/resume, source/rig changes, offsets, suppression and camera ownership.");
        }
        finally
        {
            CameraSyncDriver.TimelineDisabled = true;
        }
    }
    private static void CameraTrim()
    {
        // 1. Deadzone calculation
        const float deadzone = 0.15f;
        Func<float, float> mapStick = (float val) =>
        {
            float mag = MathF.Abs(val);
            if (mag <= deadzone) return 0f;
            return MathF.Sign(val) * ((mag - deadzone) / (1f - deadzone));
        };
        Check(mapStick(0.05f) == 0f, "Stick inside deadzone is 0");
        Check(mapStick(-0.10f) == 0f, "Stick negative inside deadzone is 0");
        Check(MathF.Abs(mapStick(0.15f)) < 0.0001f, "Stick at deadzone boundary is 0");
        Check(MathF.Abs(mapStick(1.0f) - 1.0f) < 0.0001f, "Stick full deflection is 1.0");
        Check(MathF.Abs(mapStick(-1.0f) - (-1.0f)) < 0.0001f, "Stick full negative deflection is -1.0");
        Check(mapStick(0.575f) > 0.49f && mapStick(0.575f) < 0.51f, "Stick half deflection is linear");

        // 2. Normalizing yaw
        Func<float, float> normYaw = (float val) =>
        {
            while (val > 180f) val -= 360f;
            while (val < -180f) val += 360f;
            return val;
        };
        Check(MathF.Abs(normYaw(190f) - (-170f)) < 0.001f, "Yaw 190 wraps to -170");
        Check(MathF.Abs(normYaw(-200f) - 160f) < 0.001f, "Yaw -200 wraps to 160");
        Check(MathF.Abs(normYaw(720f)) < 0.001f, "Yaw 720 wraps to 0");

        // 3. Smooth exponential damping formula: 1 - exp(-12 * dt)
        float currentYaw = 0f;
        float targetYaw = 45f;
        float dt = 1f / 60f;
        float alpha = 1f - MathF.Exp(-12f * dt);
        Check(alpha > 0.17f && alpha < 0.19f, "Single frame alpha is ~0.18 at 60fps");
        for (int i = 0; i < 30; i++)
        {
            currentYaw += (targetYaw - currentYaw) * alpha;
        }
        Check(MathF.Abs(targetYaw - currentYaw) < 0.15f, "Yaw smoothly settles near target in 0.5s");

        // 4. Reset glides back to neutral
        targetYaw = 0f;
        for (int i = 0; i < 30; i++)
        {
            currentYaw += (targetYaw - currentYaw) * alpha;
        }
        Check(MathF.Abs(currentYaw) < 0.15f, "Yaw smoothly glides back to 0 on reset");

        // 5. Origin rotation around head preserves head distance invariant
        var origin = new Transform { position = new Vector3(0, 0, -2), rotation = Quaternion.identity };
        Vector3 headWorldPos = new Vector3(0, 1.6f, 0);
        origin.RotateAround(headWorldPos, Vector3.up, 30f);
        Check(MathF.Abs(Vector3.Distance(origin.position, headWorldPos) - MathF.Sqrt(1.6f * 1.6f + 4f)) < 0.01f,
            "Distance between origin and head is invariant under RotateAround");

        // 6. Height offset applies along Vector3.up
        Vector3 prePos = origin.position;
        origin.position = origin.position + Vector3.up * 0.5f;
        Check(MathF.Abs((origin.position - prePos).value.Y - 0.5f) < 0.001f, "Height offset shifts along up axis");

        Console.WriteLine("PASS: MMD camera trim deadzone mapping, damping interpolation, wrap, and rig anchor invariants.");
    }

    private static void CardGridAndLruCache()
    {
        var destroyed = new List<string>();
        var cache = new VRCardThumbnailCache<string>(capacity: 6, onDestroy: str => destroyed.Add(str));
        Check(cache.Capacity == 6, "LRU cache initial capacity is 6");
        Check(cache.Count == 0, "LRU cache initial count is 0");

        for (int i = 1; i <= 6; i++)
        {
            cache.Put($"card_{i}.png", $"tex_{i}", $"Chara {i}");
        }
        Check(cache.Count == 6, "LRU cache count is 6 after 6 insertions");
        Check(destroyed.Count == 0, "No evictions occurred when within capacity");

        Check(cache.TryGet("card_1.png", out var item1) && item1.DisplayName == "Chara 1", "Cache hit for card_1");
        Check(!cache.TryGet("non_existent.png", out _), "Cache miss for non_existent");

        cache.Put("card_7.png", "tex_7", "Chara 7");
        Check(cache.Count == 6, "Cache count remains at capacity 6");
        Check(destroyed.Count == 1 && destroyed[0] == "tex_2", "card_2 was evicted as LRU item");
        Check(!cache.TryGet("card_2.png", out _), "card_2 is no longer in cache");
        Check(cache.TryGet("card_1.png", out _), "card_1 remains in cache because it was promoted");

        cache.Put("card_1.png", "tex_1_new", "Chara 1 Updated");
        Check(destroyed.Count == 2 && destroyed[1] == "tex_1", "Old texture destroyed on key overwrite");
        Check(cache.TryGet("card_1.png", out var itemUpdated) && itemUpdated.Texture == "tex_1_new" && itemUpdated.DisplayName == "Chara 1 Updated", "Entry updated with new texture and name");

        Check(cache.Remove("card_3.png"), "Remove existing key succeeds");
        Check(destroyed.Contains("tex_3"), "Removed texture was destroyed");
        Check(cache.Count == 5, "Count decremented after remove");

        cache.Clear();
        Check(cache.Count == 0, "Count is 0 after Clear");
        Check(destroyed.Count == 8, "All remaining items destroyed on Clear");

        const int cardsPerPage = 6;
        int totalCards = 0;
        int totalPages = Math.Max(1, (totalCards + cardsPerPage - 1) / cardsPerPage);
        Check(totalPages == 1, "0 cards gives 1 page");

        totalCards = 5;
        totalPages = Math.Max(1, (totalCards + cardsPerPage - 1) / cardsPerPage);
        Check(totalPages == 1, "5 cards gives 1 page");

        totalCards = 6;
        totalPages = Math.Max(1, (totalCards + cardsPerPage - 1) / cardsPerPage);
        Check(totalPages == 1, "6 cards gives 1 page");

        totalCards = 7;
        totalPages = Math.Max(1, (totalCards + cardsPerPage - 1) / cardsPerPage);
        Check(totalPages == 2, "7 cards gives 2 pages");

        totalCards = 270;
        totalPages = Math.Max(1, (totalCards + cardsPerPage - 1) / cardsPerPage);
        Check(totalPages == 45, "270 cards gives 45 pages");

        int maxOffset = Math.Max(0, (totalPages - 1) * cardsPerPage);
        Check(maxOffset == 44 * 6, "Max offset for 45 pages is 264");

        int offset = 0;
        offset = Math.Clamp(offset + cardsPerPage, 0, maxOffset);
        Check(offset == 6, "Offset increases by 6 on page turn");
        offset = Math.Clamp(offset - cardsPerPage, 0, maxOffset);
        Check(offset == 0, "Offset decreases back to 0");
        offset = Math.Clamp(offset - cardsPerPage, 0, maxOffset);
        Check(offset == 0, "Offset clamped at minimum 0");

        Console.WriteLine("PASS: VRCardThumbnailCache LRU eviction, promotion, destruction and grid pagination.");
    }

    private static void ThumbnailDiskCacheAndBrowserLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), "kkvr-card-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            string female = Path.Combine(root, "female");
            string nested = Path.Combine(female, "summer", "school");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.Combine(female, "archive"));
            File.WriteAllBytes(Path.Combine(female, "card.png"), new byte[] { 1, 2, 3 });

            List<VRWristFileEntry> cardsFirst = VRWristFileCatalog.ListDirectory(
                female, female, new[] { ".png" }, false, null, true);
            Check(cardsFirst.Count == 3, "Card folder lists the png and both subfolders");
            Check(!cardsFirst[0].IsDirectory, "PNG cards are listed before folders");
            Check(cardsFirst[1].IsDirectory && cardsFirst[2].IsDirectory, "Folders follow the cards");

            List<VRWristFileEntry> foldersFirst = VRWristFileCatalog.ListDirectory(
                female, female, new[] { ".png" }, false, null, false);
            Check(foldersFirst[0].IsDirectory && foldersFirst[1].IsDirectory && !foldersFirst[2].IsDirectory,
                "Folder-first mode still puts directories first");

            Check(VRWristFileCatalog.FormatFolderBadge("School") == "[📁 School]", "Folder badge wraps the name");
            Check(VRWristFileCatalog.BuildBreadcrumb(female, female) == "female", "Breadcrumb at the card root is the folder name");
            string crumb = VRWristFileCatalog.BuildBreadcrumb(female, nested);
            Check(crumb == "female  /  summer  /  school", "Breadcrumb joins child folders: " + crumb);
            Check(VRWristFileCatalog.CanAscend(female, nested), "Nested card folder can move up");
            Check(!VRWristFileCatalog.CanAscend(female, female), "Card root cannot move above itself");

            string cache = Path.Combine(root, "Thumbnails");
            VRCardThumbnailDiskCache.ResetForTests(cache);
            string cardPath = Path.Combine(female, "hello-card.png");
            File.WriteAllBytes(cardPath, System.Text.Encoding.ASCII.GetBytes("hello"));
            string md5 = VRCardThumbnailDiskCache.ComputeMd5(cardPath);
            Check(md5 == "5d41402abc4b2a76b9719d911017c592", "Content MD5 of the card bytes");
            long ticks = File.GetLastWriteTimeUtc(cardPath).Ticks;
            string cacheName = VRCardThumbnailDiskCache.BuildCacheFileName(md5, ticks, false);
            Check(cacheName == md5 + "_" + ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg",
                "Cache file name carries MD5 and write time");

            byte[] jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD0, 0x00, 0xFF, 0xD9 };
            Check(VRCardThumbnailDiskCache.Write(cardPath, jpg, md5, "Hikari"), "Disk thumbnail write succeeds");
            Check(VRCardThumbnailDiskCache.IsFresh(cardPath), "Fresh thumbnail is recognized without rereading the card");
            Check(VRCardThumbnailDiskCache.TryRead(cardPath, out byte[] read, out string displayName)
                && read.Length == jpg.Length
                && read[0] == 0xFF
                && displayName == "Hikari",
                "Disk thumbnail round-trips bytes and the character name");
            Check(File.Exists(Path.Combine(cache, cacheName.Substring(0, 2), cacheName)), "Thumbnail file uses the MD5/mtime name");

            VRCardThumbnailDiskCache.ForgetMemoryIndex();
            Check(VRCardThumbnailDiskCache.TryRead(cardPath, out _, out string reloaded) && reloaded == "Hikari",
                "Thumbnail index reloads from disk");

            File.SetLastWriteTimeUtc(cardPath, DateTime.UtcNow.AddHours(3));
            Check(!VRCardThumbnailDiskCache.IsFresh(cardPath), "Changed card write time invalidates the thumbnail");
            List<string> stale = VRCardThumbnailDiskCache.CollectStale(new[] { cardPath });
            Check(stale.Count == 1 && stale[0] == cardPath, "Background scan reports the stale card");
        }
        finally
        {
            VRCardThumbnailDiskCache.ResetForTests(null);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        Console.WriteLine("PASS: Card thumbnail disk cache, cards-before-folders, and breadcrumb navigation.");
    }

    private static void MirrorObliquePolicy()
    {
        Check(Math.Abs(VRMirrorObliquePolicy.FacingDot(0f, 0f, 2f, 0f, 0f, 1f) - 1f) < 1e-5f, "Front of the mirror faces the glass");
        Check(VRMirrorObliquePolicy.ShouldApplyOblique(1f), "Front view keeps the oblique clip");
        Check(VRMirrorObliquePolicy.FacingDot(0f, 0f, -2f, 0f, 0f, 1f) < 0f, "Behind the mirror is a negative facing dot");
        Check(!VRMirrorObliquePolicy.ShouldApplyOblique(-1f), "Behind the mirror falls back to the eye projection");
        Check(!VRMirrorObliquePolicy.ShouldApplyOblique(0f), "A view parallel to the glass falls back");
        Check(!VRMirrorObliquePolicy.ShouldApplyOblique(0.02f), "A grazing angle falls back");
        Check(VRMirrorObliquePolicy.ShouldApplyOblique(0.2f), "A clear viewing angle uses the oblique clip");
        Check(!VRMirrorObliquePolicy.ShouldApplyOblique(float.NaN), "NaN facing falls back");
        Check(!VRMirrorObliquePolicy.ShouldApplyOblique(float.PositiveInfinity), "Infinite facing falls back");
        Check(float.IsNaN(VRMirrorObliquePolicy.FacingDot(0f, 0f, 0f, 0f, 1f, 0f)), "A camera on the plane is not a usable facing dot");
        Check(VRMirrorObliquePolicy.IsStableProjection(8f), "A normal projection is stable");
        Check(!VRMirrorObliquePolicy.IsStableProjection(0f), "A zero projection is rejected");
        Check(!VRMirrorObliquePolicy.IsStableProjection(1e6f), "An exploded oblique matrix is rejected");
        Check(!VRMirrorObliquePolicy.IsStableProjection(float.NaN), "NaN projection is rejected");
        Console.WriteLine("PASS: Mirror oblique clip falls back when the camera is behind the glass or the matrix is unstable.");
    }

    private static void MirrorStereoPasses()
    {
        const int left = VRMirrorStereoPolicy.ActiveLeft;
        const int right = VRMirrorStereoPolicy.ActiveRight;
        const int mono = VRMirrorStereoPolicy.ActiveMono;

        Check(VRMirrorStereoPolicy.PassForCallback(0) == 0, "First mirror visit is the left pass");
        Check(VRMirrorStereoPolicy.PassForCallback(1) == 1, "Second mirror visit is the right pass");
        Check(VRMirrorStereoPolicy.PassForCallback(2) == 1, "Further visits stay on the right eye");
        Check(VRMirrorStereoPolicy.PassForCallback(-1) == -1, "A non-headset camera has no stereo pass");

        Check(VRMirrorStereoPolicy.Resolve(mono, false, false, false, false, 0) == VRMirrorStereoPolicy.EyeKind.Left,
            "Mono on the first pass renders the left eye");
        Check(VRMirrorStereoPolicy.Resolve(mono, false, false, false, false, 1) == VRMirrorStereoPolicy.EyeKind.Right,
            "Mono on the second pass renders the right eye");
        Check(VRMirrorStereoPolicy.TextureSlot(VRMirrorStereoPolicy.Resolve(mono, false, false, false, false, 1)) == 1,
            "The second pass binds the right reflection, not the left texture");
        Check(VRMirrorStereoPolicy.Resolve(left, true, false, true, false, 1) == VRMirrorStereoPolicy.EyeKind.Right,
            "A stale Left report on the second pass still binds the right eye");
        Check(VRMirrorStereoPolicy.Resolve(left, true, false, false, false, 0) == VRMirrorStereoPolicy.EyeKind.Left,
            "The first pass keeps the left eye when the view matches it");
        Check(VRMirrorStereoPolicy.Resolve(right, true, false, true, false, 0) == VRMirrorStereoPolicy.EyeKind.Right,
            "An explicit right eye wins on the first callback");
        Check(VRMirrorStereoPolicy.Resolve(left, false, true, false, false, 0) == VRMirrorStereoPolicy.EyeKind.Right,
            "A live view that matches the right eye binds the right reflection");
        Check(VRMirrorStereoPolicy.Resolve(mono, false, false, false, true, 0) == VRMirrorStereoPolicy.EyeKind.Right,
            "A live projection that matches only the right eye binds the right reflection");
        Check(VRMirrorStereoPolicy.Resolve(mono, false, false, false, false, -1) == VRMirrorStereoPolicy.EyeKind.Mono,
            "A desktop camera with no headset pass stays mono");

        Check(VRMirrorStereoPolicy.SteamEyeIndex(VRMirrorStereoPolicy.EyeKind.Left) == 0, "Left pose is SteamVR eyes[0]");
        Check(VRMirrorStereoPolicy.SteamEyeIndex(VRMirrorStereoPolicy.EyeKind.Right) == 1, "Right pose is SteamVR eyes[1]");
        Check(VRMirrorStereoPolicy.TextureSlot(VRMirrorStereoPolicy.EyeKind.Left) == 0, "Left eye samples the left reflection");
        Check(VRMirrorStereoPolicy.TextureSlot(VRMirrorStereoPolicy.EyeKind.Right) == 1, "Right eye samples the right reflection");

        Check(VRMirrorStereoPolicy.SelectView(true, true, true, false) == VRMirrorStereoPolicy.ViewSource.Live,
            "The rasterizer's live view is used when it is this eye");
        Check(VRMirrorStereoPolicy.SelectView(false, true, true, false) == VRMirrorStereoPolicy.ViewSource.UnityStereo,
            "A distinct stereo pair supplies the eye that is not currently live");
        Check(VRMirrorStereoPolicy.SelectView(false, false, true, false) == VRMirrorStereoPolicy.ViewSource.UnityStereo,
            "Projection shear uses the shared stereo view so IPD is not applied twice");
        Check(VRMirrorStereoPolicy.SelectView(false, false, false, false) == VRMirrorStereoPolicy.ViewSource.SteamVr,
            "Identical Unity eyes fall back to the SteamVR eye pose");
        Check(VRMirrorStereoPolicy.SelectView(false, false, false, true) == VRMirrorStereoPolicy.ViewSource.Live,
            "A mono camera keeps its own view");
        Check(VRMirrorStereoPolicy.SelectProj(false, false, false) == VRMirrorStereoPolicy.ProjSource.SteamRaw,
            "The right eye oblique matrix uses that eye's own frustum when Unity's projections match");
        Check(VRMirrorStereoPolicy.SelectProj(false, true, false) == VRMirrorStereoPolicy.ProjSource.UnityStereo,
            "A distinct stereo projection is the one the eye will sample with");
        Check(VRMirrorStereoPolicy.SelectProj(true, true, false) == VRMirrorStereoPolicy.ProjSource.Live,
            "A live projection that already matches this eye is kept");

        Check(VRMirrorStereoPolicy.ShouldRender(-1, 10, VRMirrorStereoPolicy.ViewSource.Live, VRMirrorStereoPolicy.ViewSource.SteamVr),
            "A texture from a previous frame is rendered again");
        Check(!VRMirrorStereoPolicy.ShouldRender(10, 10, VRMirrorStereoPolicy.ViewSource.UnityStereo, VRMirrorStereoPolicy.ViewSource.UnityStereo),
            "The same stereo pose is not rendered twice in one frame");
        Check(VRMirrorStereoPolicy.ShouldRender(10, 10, VRMirrorStereoPolicy.ViewSource.UnityStereo, VRMirrorStereoPolicy.ViewSource.Live),
            "A live eye matrix replaces a prefetched stereo render");
        Check(!VRMirrorStereoPolicy.ShouldRender(10, 10, VRMirrorStereoPolicy.ViewSource.Live, VRMirrorStereoPolicy.ViewSource.UnityStereo),
            "A live eye render is not replaced by the stereo pair");

        Check(VRMirrorStereoPolicy.TryRawFrustum(-1.2f, 0.8f, 1f, -1f, 0.1f, 100f, out float l, out float r, out float b, out float t),
            "An asymmetric right-eye tangent pair builds a frustum");
        Check(Math.Abs(l - (-0.12f)) < 1e-5f && Math.Abs(r - 0.08f) < 1e-5f && Math.Abs(b - (-0.1f)) < 1e-5f && Math.Abs(t - 0.1f) < 1e-5f,
            "Frustum edges sit on the near plane");
        Check(VRMirrorStereoPolicy.TryRawFrustum(-1f, 1f, -1f, 1f, 0.1f, 100f, out _, out _, out float swappedBottom, out float swappedTop)
            && swappedBottom < swappedTop,
            "Swapped OpenVR top and bottom are ordered for Unity");
        Check(!VRMirrorStereoPolicy.TryRawFrustum(1f, -1f, 1f, -1f, 0.1f, 100f, out _, out _, out _, out _),
            "A flipped horizontal frustum is rejected");
        Check(!VRMirrorStereoPolicy.TryRawFrustum(-1f, 1f, 1f, -1f, 0f, 100f, out _, out _, out _, out _),
            "A zero near plane is rejected");

        Console.WriteLine("PASS: Mirror stereo passes bind a separate right-eye reflection and projection.");
    }

    private static void MmdDanceRatingsAndCategories()
    {
        // 1. Built-in Masterpiece Seed Verification
        VRMmdDanceRatingDatabase db = new VRMmdDanceRatingDatabase();
        VRMmdDanceRatingStore.SeedBuiltInMasterpieces(db);
        db.RebuildIndex();
        Check(db.Entries.Count >= 20, "Built-in database has at least 20 masterpieces");

        VRMmdDanceRatingEntry gokuraku;
        Check(db.TryGetEntry("極楽浄土（３人組）", out gokuraku), "Found Gokuraku Jodo in db");
        Check(gokuraku.Rating == 9.0f, "Gokuraku rating is 9.0★");
        Check(gokuraku.Category == VRMmdDanceCategories.Masterpiece, "Gokuraku category is Masterpiece");
        Check(gokuraku.Favorite, "Gokuraku is marked Favorite");

        VRMmdDanceRatingEntry rabbit;
        Check(db.TryGetEntry("兔子洞ラビットホール", out rabbit), "Found Rabbit Hole in db");
        Check(rabbit.Rating == 9.0f, "Rabbit hole rating is 9.0★");

        VRMmdDanceRatingEntry jumpUp;
        Check(db.TryGetEntry("JUMP UP", out jumpUp), "Found JUMP UP in db");
        Check(jumpUp.Rating == 8.9f, "JUMP UP rating is 8.9★");
        Check(jumpUp.IsMoCap, "JUMP UP is MoCap");
        Check(jumpUp.Category == VRMmdDanceCategories.MoCap, "JUMP UP category is MoCap");

        VRMmdDanceRatingEntry supernova;
        Check(db.TryGetEntry("aespa - Supernova", out supernova), "Found Supernova in db");
        Check(supernova.Rating == 8.8f, "Supernova rating is 8.8★");
        Check(supernova.Category == VRMmdDanceCategories.HighEnergy, "Supernova category is HighEnergy");

        VRMmdDanceRatingEntry sweetDevil;
        Check(db.TryGetEntry("sweet devil h~~", out sweetDevil), "Found Sweet Devil in db");
        Check(sweetDevil.Rating == 8.5f, "Sweet devil rating is 8.5★");
        Check(sweetDevil.Category == VRMmdDanceCategories.Gentlemen, "Sweet devil category is Gentlemen");

        // 2. Heuristic Scoring Engine Verification
        float scoreComplete = VRMmdHeuristicScorer.CalculateScore("普通动作A", "", true, true, 3000, 150, false, null);
        Check(scoreComplete >= 7.0f && scoreComplete <= 8.5f, "Complete standard motion gets 7.0~8.5 base rating");

        float scoreMissingCamAndAudio = VRMmdHeuristicScorer.CalculateScore("缺失动作B", "", false, false, 300, 0, false, null);
        Check(scoreMissingCamAndAudio < 5.0f, "Missing camera and audio gets low rating");

        float scoreMoCap = VRMmdHeuristicScorer.CalculateScore("真实街舞", "动捕 60fps", true, true, 4000, 200, true, null);
        Check(scoreMoCap >= 8.0f, "Motion capture with 60fps gets high rating boost");

        float scoreMasterpiece = VRMmdHeuristicScorer.CalculateScore("极乐净土特别版", "", true, true, 3600, 150, false, null);
        Check(scoreMasterpiece >= 8.5f, "Masterpiece keyword gets 8.5+ rating boost");

        // Clamp checks
        Check(VRMmdHeuristicScorer.ClampRating(12.5f) == 9.0f, "Clamp rating max 9.0★");
        Check(VRMmdHeuristicScorer.ClampRating(-4.2f) == 1.0f, "Clamp rating min 1.0★");
        Check(VRMmdHeuristicScorer.ClampRating(8.5432f) == 8.5f, "Clamp rating rounds to 1 decimal place");

        // Category detection
        Check(VRMmdHeuristicScorer.DetectCategory("普通舞", "", 9.0f, false, "") == VRMmdDanceCategories.Masterpiece, "Rating >= 8.5 detects Masterpiece");
        Check(VRMmdHeuristicScorer.DetectCategory("JUMP UP", "", 8.0f, true, "") == VRMmdDanceCategories.MoCap, "MoCap flag detects MoCap category");
        Check(VRMmdHeuristicScorer.DetectCategory("aespa - Whiplash", "", 7.5f, false, "r18") == VRMmdDanceCategories.Gentlemen, "R18 detects Gentlemen category");
        Check(VRMmdHeuristicScorer.DetectCategory("抖音卡点舞", "", 7.0f, false, "慢摇") == VRMmdDanceCategories.SexyGroove, "Shaking / TikTok detects SexyGroove");
        Check(VRMmdHeuristicScorer.DetectCategory("千本樱扇子舞", "", 7.5f, false, "古风") == VRMmdDanceCategories.GentleSlow, "Ancient / fan detects GentleSlow");
        Check(VRMmdHeuristicScorer.DetectCategory("BLACKPINK - Pink Venom", "", 7.5f, false, "") == VRMmdDanceCategories.HighEnergy, "K-pop girl group detects HighEnergy");

        // Category cycling
        Check(VRMmdDanceCategories.GetNext(VRMmdDanceCategories.All) == VRMmdDanceCategories.Masterpiece, "Category next from All is Masterpiece");
        Check(VRMmdDanceCategories.GetPrev(VRMmdDanceCategories.Masterpiece) == VRMmdDanceCategories.All, "Category prev from Masterpiece is All");

        // 3. Category Filtering & 9★ -> 1★ Descending Sort Verification
        var allDesc = db.GetFilteredEntries(VRMmdDanceCategories.All, false, true);
        Check(allDesc.Count == db.Entries.Count, "All category returns all entries");
        for (int i = 0; i < allDesc.Count - 1; i++)
        {
            Check(allDesc[i].Rating >= allDesc[i + 1].Rating, "Entries strictly sorted descending by rating (9.0 -> 1.0)");
        }
        Check(allDesc[0].Rating == 9.0f, "First entry is 9.0★ top masterpiece");

        var masterpieces = db.GetFilteredEntries(VRMmdDanceCategories.Masterpiece, false, true);
        Check(masterpieces.Count > 0, "Masterpieces category has entries");
        foreach (var m in masterpieces)
        {
            Check(m.Rating >= 8.5f || m.Favorite || m.Category == VRMmdDanceCategories.Masterpiece, "All entries in Masterpieces have rating >= 8.5 or are favorite");
        }

        var mocaps = db.GetFilteredEntries(VRMmdDanceCategories.MoCap, false, true);
        Check(mocaps.Count > 0, "MoCap category has entries");
        Check(mocaps[0].Rating >= 8.8f, "Top MoCap entry is high rated (8.8+★)");

        // 4. Live Rating Adjustment (+1 / -1 Star) and Favorite
        float originalRating = jumpUp.Rating;
        var lowered = db.AdjustRating("JUMP UP", -1.0f);
        Check(lowered.Rating == VRMmdHeuristicScorer.ClampRating(originalRating - 1.0f), "Adjust -1.0★ decreases score by 1");
        Check(lowered.UserRatingAdjust == -1, "User rating adjust tracks count");

        var raised = db.AdjustRating("JUMP UP", +2.0f);
        Check(raised.Rating == VRMmdHeuristicScorer.ClampRating(lowered.Rating + 2.0f), "Adjust +2.0★ increases score");

        var maxClamped = db.AdjustRating("JUMP UP", +10.0f);
        Check(maxClamped.Rating == 9.0f, "Adjust rating clamped at 9.0★");

        var minClamped = db.AdjustRating("JUMP UP", -10.0f);
        Check(minClamped.Rating == 1.0f, "Adjust rating clamped at 1.0★");

        // Restore JUMP UP rating
        db.AdjustRating("JUMP UP", originalRating - minClamped.Rating);

        // Toggle Favorite
        bool origFav = supernova.Favorite;
        var favToggled = db.ToggleFavorite("aespa - Supernova");
        Check(favToggled.Favorite == !origFav, "Favorite state toggled");
        db.ToggleFavorite("aespa - Supernova"); // toggle back

        // 5. JSON Serialization & Deserialization Round-Trip
        string json = VRMmdDanceRatingStore.SerializeToJson(db);
        Check(json.Contains("\"version\": 1"), "JSON contains version");
        Check(json.Contains("\"motionName\": \"極楽浄土（３人組）\""), "JSON contains Gokuraku");
        Check(json.Contains("\"rating\": 9.0"), "JSON contains 9.0 rating");

        VRMmdDanceRatingDatabase loadedDb = VRMmdDanceRatingStore.DeserializeFromJson(json);
        Check(loadedDb.Entries.Count == db.Entries.Count, "Deserialized entry count matches original");
        loadedDb.RebuildIndex();

        VRMmdDanceRatingEntry loadedGokuraku;
        Check(loadedDb.TryGetEntry("極楽浄土（３人組）", out loadedGokuraku), "Loaded DB finds Gokuraku");
        Check(loadedGokuraku.Rating == 9.0f, "Loaded Gokuraku rating is 9.0★");
        Check(loadedGokuraku.Description.Contains("ACG传世神作"), "Loaded Gokuraku description preserved");

        // 6. CSV Seeding Verification (from real E:\action\动作全量分类索引.csv if present)
        string csvPath = @"E:\action\动作全量分类索引.csv";
        if (File.Exists(csvPath))
        {
            VRMmdDanceRatingDatabase csvDb = new VRMmdDanceRatingDatabase();
            int imported = VRMmdDanceRatingStore.SeedFromCsv(csvDb, csvPath);
            Check(imported >= 1900, "Imported 1900+ items from 动作全量分类索引.csv");

            VRMmdDanceRatingEntry queencard;
            Check(csvDb.TryGetEntry("(G)I-DLE - Queencard", out queencard), "CSV imported Queencard");
            Check(queencard.Rating >= 7.0f, "Queencard rating is >= 7.0★ in CSV import");

            var csvMasterpieces = csvDb.GetFilteredEntries(VRMmdDanceCategories.Masterpiece, false, true);
            Check(csvMasterpieces.Count > 0, "CSV has Masterpieces filtered");
            Check(csvMasterpieces[0].Rating >= 8.5f, "CSV top masterpiece is >= 8.5★");
        }

        // 7. Disk Save & Load Persistence
        string tempJsonPath = Path.Combine(Path.GetTempPath(), "mmd_test_ratings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            VRMmdDanceRatingStore.SaveDatabase(tempJsonPath, db);
            Check(File.Exists(tempJsonPath), "Saved database file exists on disk");
            string savedJson = File.ReadAllText(tempJsonPath);
            Check(savedJson.Contains("兔子洞ラビットホール"), "Saved JSON file contains Rabbit Hole");

            VRMmdDanceRatingStore.LoadDatabase(tempJsonPath);
            Check(VRMmdDanceRatingStore.Database.Entries.Count == db.Entries.Count, "Database loaded from disk matches entry count");
        }
        finally
        {
            try { if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath); } catch { }
            try { if (File.Exists(tempJsonPath + ".bak")) File.Delete(tempJsonPath + ".bak"); } catch { }
        }

        Console.WriteLine("PASS: MMD Dance Rating & Category System (1~9 stars, heuristic scoring, JSON storage, category filter, 9★->1★ top-sorting).");
    }

    private static void StickTransportAndIkInteraction()
    {
        Check(VRStudioInteractionPolicy.CanToggle(true, false, false, 0f, 0f, false),
            "A latched MMD session can resume after the playback report goes stale");
        Check(VRStudioInteractionPolicy.CanToggle(false, true, true, 0f, 120f, false),
            "A fresh paused timeline with frames can be started");
        Check(!VRStudioInteractionPolicy.CanToggle(false, true, true, 0f, 0f, false),
            "An empty MMD timeline does not own the stick");
        Check(VRStudioInteractionPolicy.CanToggle(false, true, true, 0f, 10f, true),
            "Fresh playback can be paused");

        VRStudioInteractionPolicy.StickPlan playing = VRStudioInteractionPolicy.PlanStick(
            true, true, true, true, false, false);
        Check(playing.Operation == VRStudioInteractionPolicy.TransportOp.Pause
            && playing.SwallowLocomotion && playing.HoldUntilRelease,
            "A click while playing pauses and holds the stick until release");

        VRStudioInteractionPolicy.StickPlan paused = VRStudioInteractionPolicy.PlanStick(
            true, true, true, false, false, false);
        Check(paused.Operation == VRStudioInteractionPolicy.TransportOp.Start
            && paused.SwallowLocomotion && paused.HoldUntilRelease,
            "A click while paused starts playback instead of turning the view");

        VRStudioInteractionPolicy.StickPlan stale = VRStudioInteractionPolicy.PlanStick(
            true, true, false, false, false, false);
        Check(stale.Operation == VRStudioInteractionPolicy.TransportOp.ToggleLive
            && stale.SwallowLocomotion && stale.HoldUntilRelease,
            "A stale paused report uses the live toggle and still swallows locomotion");

        VRStudioInteractionPolicy.StickPlan empty = VRStudioInteractionPolicy.PlanStick(
            true, false, true, false, false, false);
        Check(empty.Operation == VRStudioInteractionPolicy.TransportOp.Ignore
            && empty.SwallowLocomotion && !empty.HoldUntilRelease,
            "A click with no MMD session does not keep owning the stick");

        VRStudioInteractionPolicy.StickPlan held = VRStudioInteractionPolicy.PlanStick(
            false, true, true, false, false, true);
        Check(held.Operation == VRStudioInteractionPolicy.TransportOp.Ignore
            && held.SwallowLocomotion && held.HoldUntilRelease,
            "Stick deflection after a transport click cannot move the view");

        VRStudioInteractionPolicy.StickPlan idle = VRStudioInteractionPolicy.PlanStick(
            false, true, true, false, false, false);
        Check(!idle.SwallowLocomotion && !idle.HoldUntilRelease,
            "A released stick with no click is normal locomotion");

        VRStudioInteractionPolicy.StickPlan second = VRStudioInteractionPolicy.PlanStick(
            true, true, true, false, true, true);
        Check(second.Operation == VRStudioInteractionPolicy.TransportOp.Ignore
            && second.SwallowLocomotion && second.HoldUntilRelease,
            "The same click cannot start playback twice");

        Check(!VRStudioInteractionPolicy.IkRendererEnabled(false)
            && VRStudioInteractionPolicy.IkColliderEnabled(false),
            "Hidden IK guides keep their colliders");
        Check(VRStudioInteractionPolicy.IkRendererEnabled(true)
            && VRStudioInteractionPolicy.IkColliderEnabled(true),
            "Visible IK guides render and stay collidable");
        Check(VRStudioInteractionPolicy.DynamicTouchAllowed(true)
            && !VRStudioInteractionPolicy.DynamicTouchAllowed(false),
            "Dynamic touch is an independent switch");
        Check(VRStudioInteractionPolicy.KneadTouchAllowed(true, false)
            && !VRStudioInteractionPolicy.PhysicalUndressAllowed(true, false),
            "Physical undress off blocks clothes removal while knead and touch stay allowed");
        Check(!VRStudioInteractionPolicy.KneadTouchAllowed(false, true)
            && VRStudioInteractionPolicy.PhysicalUndressAllowed(false, true),
            "Knead and touch off still allow physical undress");
        Check(VRStudioInteractionPolicy.KneadTouchAllowed(true, true)
            && VRStudioInteractionPolicy.PhysicalUndressAllowed(true, true),
            "Both switches on allow knead, touch, and physical undress");
        Check(!VRStudioInteractionPolicy.KneadTouchAllowed(false, false)
            && !VRStudioInteractionPolicy.PhysicalUndressAllowed(false, false),
            "Both switches off block knead, touch, and physical undress");
        Check(VRStudioInteractionPolicy.BakePoseOnRelease(true)
            && !VRStudioInteractionPolicy.RestituteSoftBody(true, true),
            "Figure posing bakes the release pose and skips soft-body spring");
        Check(!VRStudioInteractionPolicy.BakePoseOnRelease(false)
            && VRStudioInteractionPolicy.RestituteSoftBody(false, true)
            && !VRStudioInteractionPolicy.RestituteSoftBody(false, false),
            "Physics mode springs soft tissue only while dynamic touch is on");

        // Guide visibility: the native Studio switch follows the VR switch, the
        // colliders never follow it, and Studio's own hiding still wins.
        Check(!VRStudioInteractionPolicy.NativeGuideVisible(false)
            && VRStudioInteractionPolicy.NativeGuideVisible(true),
            "The native Studio guide switch follows the VR switch");
        Check(!VRStudioInteractionPolicy.MayActivateHiddenGizmo(true)
            && !VRStudioInteractionPolicy.MayActivateHiddenGizmo(false),
            "Hiding guides never reactivates a gizmo Studio deactivated");
        Check(VRStudioInteractionPolicy.GuideRendererEnabled(true, true, true),
            "A visible guide on a visible character renders");
        Check(!VRStudioInteractionPolicy.GuideRendererEnabled(false, true, true),
            "The VR switch hides a guide Studio would otherwise draw");
        Check(!VRStudioInteractionPolicy.GuideRendererEnabled(true, true, false)
            && !VRStudioInteractionPolicy.GuideRendererEnabled(true, false, true),
            "Showing guides again does not reveal ones Studio is hiding");

        // Limb posing: the dragged chain has to be handed to the solver, or the
        // Animator pulls the leg back to the clip on the next frame.
        Check(VRStudioInteractionPolicy.PlanPoseChain(true, false) == VRStudioInteractionPolicy.PoseChain.Ik
            && VRStudioInteractionPolicy.PlanPoseChain(false, true) == VRStudioInteractionPolicy.PoseChain.Fk
            && VRStudioInteractionPolicy.PlanPoseChain(false, false) == VRStudioInteractionPolicy.PoseChain.None,
            "An IK target picks the IK chain, a bone picks FK, anything else picks neither");
        Check(VRStudioInteractionPolicy.NeedsKinematicModeSwitch(VRStudioInteractionPolicy.PoseChain.Ik, false, true),
            "Dragging an IK target while the character is in FK switches the mode");
        Check(!VRStudioInteractionPolicy.NeedsKinematicModeSwitch(VRStudioInteractionPolicy.PoseChain.Ik, true, false),
            "A character already in IK mode is left alone");
        Check(VRStudioInteractionPolicy.NeedsKinematicModeSwitch(VRStudioInteractionPolicy.PoseChain.Fk, false, false)
            && !VRStudioInteractionPolicy.NeedsKinematicModeSwitch(VRStudioInteractionPolicy.PoseChain.None, false, false),
            "A bone on a character with no kinematics switches to FK, a non-character guide switches nothing");
        Check(!VRStudioInteractionPolicy.NeedsKinematicModeSwitch(VRStudioInteractionPolicy.PoseChain.Fk, true, false),
            "Grabbing a bone never tears down an IK pose the player already built");
        Check(VRStudioInteractionPolicy.NeedsGroupActivation(VRStudioInteractionPolicy.PoseChain.Ik, false)
            && !VRStudioInteractionPolicy.NeedsGroupActivation(VRStudioInteractionPolicy.PoseChain.Ik, true)
            && !VRStudioInteractionPolicy.NeedsGroupActivation(VRStudioInteractionPolicy.PoseChain.None, false),
            "An inactive limb group is activated once and only once");
        Check(VRStudioInteractionPolicy.ShouldFreezeAnimation(true, 1f)
            && !VRStudioInteractionPolicy.ShouldFreezeAnimation(true, 0f)
            && !VRStudioInteractionPolicy.ShouldFreezeAnimation(false, 1f),
            "Figure posing stops a running clip and leaves a still character alone");
        Check(VRStudioInteractionPolicy.HoldReleasedPose(true)
            && !VRStudioInteractionPolicy.HoldReleasedPose(false),
            "Figure posing keeps re-asserting the released pose");
        Check(VRStudioInteractionPolicy.KeepHeldPose(true, false)
            && !VRStudioInteractionPolicy.KeepHeldPose(true, true)
            && !VRStudioInteractionPolicy.KeepHeldPose(false, false),
            "A re-grabbed guide drops its hold so the lock cannot fight the hand");

        Console.WriteLine("PASS: paused MMD stick resumes, IK colliders stay live, guide visibility reaches the native spheres, dragged limbs claim their solver, figure posing holds the released pose.");
    }

    private static void ReviewRegressions()
    {
        // Rating lookup: queries with no letters/digits and very short names must not fuzzy-match everything.
        VRMmdDanceRatingDatabase db = new VRMmdDanceRatingDatabase();
        db.AddOrUpdate(new VRMmdDanceRatingEntry { MotionName = "Gokuraku Jodo Dance", RelativePath = "gokuraku" });
        db.AddOrUpdate(new VRMmdDanceRatingEntry { MotionName = "ab", RelativePath = "ab" });
        VRMmdDanceRatingEntry found;
        Check(!db.TryGetEntry("___", out found) && found == null, "Punctuation-only query matches nothing");
        Check(!db.TryGetEntry("!!!", out found), "Symbol query does not return an arbitrary entry");
        Check(!db.TryGetEntry("xab", out found), "Two-letter motion names are excluded from substring matching");
        Check(db.TryGetEntry("Gokuraku Jodo", out found) && found.MotionName == "Gokuraku Jodo Dance", "Substring lookup still finds long names");
        Check(db.TryGetEntry("Gokuraku Jodo", out found) && found.MotionName == "Gokuraku Jodo Dance", "Cached fuzzy lookup stays correct");
        db.AddOrUpdate(new VRMmdDanceRatingEntry { MotionName = "Gokuraku Jodo Extended", RelativePath = "gokuraku2" });
        Check(db.TryGetEntry("Gokuraku Jodo Extended Cut", out found) && found.MotionName == "Gokuraku Jodo Extended", "Adding an entry refreshes lookup caches");
        Check(!db.TryGetEntry("totally unrelated", out found), "Misses are stable");
        db.AddOrUpdate(new VRMmdDanceRatingEntry { MotionName = "Zeta Long Name", RelativePath = "z" });
        Check(db.TryGetEntry("Zeta Long Name Remix", out found), "A previous miss cache does not hide a later added entry");

        // Whole-word keyword scoring.
        float latest = VRMmdHeuristicScorer.CalculateScore("The Latest Contest Motion", "", true, true, 3000, 0, false, null);
        float tested = VRMmdHeuristicScorer.CalculateScore("Motion test", "", true, true, 3000, 0, false, null);
        Check(latest > tested + 1f, "Words containing 'test' are not penalised like a test motion");
        Check(VRMmdHeuristicScorer.DetectCategory("Active Creative Dance", "", 5f, false, "") == VRMmdDanceCategories.OtakuDance,
            "'ive' inside other words does not select HighEnergy");
        Check(VRMmdHeuristicScorer.DetectCategory("IVE - Love Dive", "", 5f, false, "") == VRMmdDanceCategories.HighEnergy,
            "The IVE group name still selects HighEnergy");
        Check(VRMmdHeuristicScorer.ClampRating(float.NaN) == 5.5f, "NaN rating falls back to neutral");

        // JSON: a path ending in a backslash must not break the object scan.
        VRMmdDanceRatingDatabase source = new VRMmdDanceRatingDatabase();
        source.AddOrUpdate(new VRMmdDanceRatingEntry { MotionName = "First", FullPath = "E:\\\\dance\\\\", RelativePath = "First", Rating = 7.0f });
        source.AddOrUpdate(new VRMmdDanceRatingEntry { MotionName = "Second", FullPath = "E:\\\\dance2", RelativePath = "Second", Rating = 6.0f, Description = "say \"hi\"" });
        VRMmdDanceRatingDatabase round = VRMmdDanceRatingStore.DeserializeFromJson(VRMmdDanceRatingStore.SerializeToJson(source));
        Check(round.Entries.Count == 2, "Backslash-terminated path keeps both entries after a JSON round trip");
        Check(round.Entries[0].FullPath == source.Entries[0].FullPath, "Backslash-terminated path round trips");
        Check(round.Entries[1].Description == "say \"hi\"", "Escaped quotes round trip");
        string unicode = "{\"entries\":[{\"motionName\":\"\\u4e2d\\u6587\",\"rating\":6.5}]}";
        Check(VRMmdDanceRatingStore.DeserializeFromJson(unicode).Entries[0].MotionName == "\u4e2d\u6587", "\\uXXXX escapes decode");

        // A damaged ratings file is preserved, never replaced silently by the seed.
        string dir = Path.Combine(Path.GetTempPath(), "kks_review_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "ratings.json");
            string damaged = "{ \"entries\": [ { \"motionName\": \"broken " + new string('x', 200);
            File.WriteAllText(file, damaged);
            VRMmdDanceRatingStore.LoadDatabase(file);
            Check(Directory.GetFiles(dir, "ratings.json.corrupt-*").Length == 1, "Damaged ratings file is kept aside");
            Check(File.Exists(file + ".bak") || File.Exists(file), "A fresh database is written after preserving the damaged one");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        // Thumbnail cache owns textures it refuses, and disk index writes are batched but flushable.
        List<string> destroyed = new List<string>();
        VRCardThumbnailCache<string> cache = new VRCardThumbnailCache<string>(6, str => destroyed.Add(str));
        cache.Put("", "orphan", "name");
        Check(destroyed.Count == 1 && destroyed[0] == "orphan" && cache.Count == 0, "Rejected cache key releases the texture");

        string root = Path.Combine(Path.GetTempPath(), "kks_thumb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cacheDir = Path.Combine(root, "Thumbnails");
            VRCardThumbnailDiskCache.ResetForTests(cacheDir);
            string[] cards = new string[3];
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = Path.Combine(root, "card" + i + ".png");
                File.WriteAllBytes(cards[i], new byte[] { 0x89, 0x50, 0x4E, 0x47, (byte)i, 1, 2, 3 });
                byte[] thumb = new byte[] { 0xFF, 0xD8, 0xFF, (byte)i, 9, 9, 9, 9 };
                Check(VRCardThumbnailDiskCache.Write(cards[i], thumb, null, "Card" + i), "Batched thumbnail write " + i);
            }
            VRCardThumbnailDiskCache.Flush();
            VRCardThumbnailDiskCache.ForgetMemoryIndex();
            for (int i = 0; i < cards.Length; i++)
                Check(VRCardThumbnailDiskCache.IsFresh(cards[i]), "Flushed index keeps thumbnail " + i);
            Check(!File.Exists(Path.Combine(cacheDir, "index.tsv.tmp")), "Atomic index swap leaves no temp file");
        }
        finally
        {
            VRCardThumbnailDiskCache.ResetForTests(null);
            try { Directory.Delete(root, true); } catch { }
        }

        // Stick transport: a held click keeps owning the stick, a new click after release works.
        VRStudioInteractionPolicy.StickPlan held = VRStudioInteractionPolicy.PlanStick(true, true, true, true, true, true);
        Check(held.Operation == VRStudioInteractionPolicy.TransportOp.Ignore && held.HoldUntilRelease && held.SwallowLocomotion,
            "A click while the previous click is still held is ignored and keeps the hold");
        VRStudioInteractionPolicy.StickPlan next = VRStudioInteractionPolicy.PlanStick(true, true, true, true, false, false);
        Check(next.Operation == VRStudioInteractionPolicy.TransportOp.Pause, "After the hold is released a click pauses playback");
        Check(VRStudioInteractionPolicy.TransportOwnsHeldPress(held, true),
            "A sticky down edge during the transport hold is reported as handled, so it cannot fall through to undo");
        VRStudioInteractionPolicy.StickPlan noMmd = VRStudioInteractionPolicy.PlanStick(true, false, false, false, false, false);
        Check(noMmd.Operation == VRStudioInteractionPolicy.TransportOp.Ignore
            && !VRStudioInteractionPolicy.TransportOwnsHeldPress(noMmd, false),
            "Without MMD and without a hold the right-stick click stays available for undo/Timeline");
        Check(!VRStudioInteractionPolicy.TransportOwnsHeldPress(next, false),
            "An operation that toggles is not reported as a held-press swallow");
        Console.WriteLine("PASS: review regressions (fuzzy lookup, JSON escapes, damaged ratings file, thumbnail ownership, stick hold).");
    }

    private static void ClothingPresetHitTest()
    {
        // The wrist canvas is 560x500 with Unity's centred pivot, so its local
        // rect runs x -280..280 and y -250..250. Layout is authored top-left down.
        const float xMin = -280f, yMax = 250f, width = 560f;
        const float viewportTop = 158f, viewportHeight = 250f;
        Func<float, float, float, bool> hit = (contentX, contentY, scroll) =>
        {
            float menuY = VRWristMenuHitPolicy.ContentToMenuY(contentY, viewportTop, scroll);
            float localX = contentX + xMin;
            float localY = yMax - menuY;
            return VRWristMenuHitPolicy.InsideViewport(localX, localY, xMin, yMax, width, viewportTop, viewportHeight);
        };

        Check(VRWristMenuHitPolicy.ContentToMenuY(24f, viewportTop, 0f) == 182f,
            "Dress-state row sits at menu y 182 when the list is not scrolled");
        Check(hit(24f + 82.5f, 24f + 20f, 0f), "Fully dressed button centre is inside the clothing viewport");
        Check(hit(198f + 82f, 24f + 20f, 0f), "Half dressed button centre is inside the clothing viewport");
        Check(hit(371f + 82.5f, 24f + 20f, 0f), "Undressed button centre is inside the clothing viewport");
        Check(hit(25f, 25f, 0f) && hit(535f, 63f, 0f), "Dress-state button corners are inside the clothing viewport");

        float centreLocalY = yMax - VRWristMenuHitPolicy.ContentToMenuY(44f, viewportTop, 0f);
        Check(!VRWristMenuHitPolicy.InsideBand(-centreLocalY, viewportTop, viewportHeight),
            "Treating -local.y as the top-down coordinate (the old check) rejects the dress-state buttons");
        Check(VRWristMenuHitPolicy.InsideBand(VRWristMenuHitPolicy.LogicalY(centreLocalY, yMax), viewportTop, viewportHeight),
            "Converting through the rect top accepts the dress-state buttons");

        Check(!hit(106f, 44f, 100f), "A dress-state button scrolled above the viewport is not clickable");
        Check(!hit(152f, 291f, 0f), "A fine-control row below the viewport is clipped");
        Check(hit(152f, 291f, 100f), "The same row becomes clickable once scrolled into view");
        Check(!VRWristMenuHitPolicy.InsideViewport(0f, yMax - 440f, xMin, yMax, width, viewportTop, viewportHeight),
            "The status bar is outside the clothing viewport");
        Check(!VRWristMenuHitPolicy.InsideViewport(0f, yMax - 30f, xMin, yMax, width, viewportTop, viewportHeight),
            "The header is outside the clothing viewport");
        Check(!VRWristMenuHitPolicy.InsideViewport(xMin - 20f, yMax - 202f, xMin, yMax, width, viewportTop, viewportHeight),
            "A point left of the panel is outside the viewport");
        Console.WriteLine("PASS: clothing dress-state buttons pass the scroll-viewport visibility check on a centre-pivot canvas.");
    }

    private static void MmdFreeLocomotion()
    {
        VRStudioInteractionPolicy.MmdMovementPlan dance = VRStudioInteractionPolicy.PlanMmdMovement(true, false);
        Check(!dance.LockRig && dance.AllowLocomotion && dance.AllowWorldGrab,
            "A dance without a camera VMD keeps stick locomotion and grip world-move");
        Check(!dance.StickTrimsCamera, "Without a camera motion the right stick turns the player, not a camera trim");
        Check(!dance.AllowObjectGrab, "Limb grabs stay off while a dance plays");

        VRStudioInteractionPolicy.MmdMovementPlan camera = VRStudioInteractionPolicy.PlanMmdMovement(true, true);
        Check(camera.LockRig && camera.StickTrimsCamera, "A loaded camera motion anchors the rig and owns the trim stick");
        Check(!camera.AllowLocomotion && !camera.AllowWorldGrab && !camera.AllowObjectGrab,
            "A camera motion blocks locomotion, world-move, and grabs");

        VRStudioInteractionPolicy.MmdMovementPlan paused = VRStudioInteractionPolicy.PlanMmdMovement(false, true);
        Check(!paused.LockRig && paused.AllowLocomotion && paused.AllowWorldGrab && paused.AllowObjectGrab,
            "A paused clip with a camera loaded returns full control");
        VRStudioInteractionPolicy.MmdMovementPlan idle = VRStudioInteractionPolicy.PlanMmdMovement(false, false);
        Check(!idle.LockRig && idle.AllowLocomotion && idle.AllowObjectGrab, "No MMD session means normal input");
        Console.WriteLine("PASS: MMD without a camera motion keeps free locomotion; only a camera VMD anchors the rig.");
    }

    private static void FigureScale()
    {
        Func<float, float, bool> near = (a, b) => MathF.Abs(a - b) < 1e-4f;
        Check(near(VRFigureScalePolicy.PresetScale(1), 1f), "1/1 is life size");
        Check(near(VRFigureScalePolicy.PresetScale(4), 0.25f), "1/4 is 0.25x");
        Check(near(VRFigureScalePolicy.PresetScale(6), 0.1667f), "1/6 is 0.167x");
        Check(near(VRFigureScalePolicy.PresetScale(7), 0.142857f), "1/7 is 0.143x");
        Check(near(VRFigureScalePolicy.PresetScale(12), 0.083333f), "1/12 is 0.083x");
        foreach (int denominator in VRFigureScalePolicy.PresetDenominators)
            Check(VRFigureScalePolicy.MatchPreset(VRFigureScalePolicy.PresetScale(denominator)) == denominator,
                "Preset 1/" + denominator + " is recognised");
        Check(VRFigureScalePolicy.MatchPreset(0.143f) == 7 && VRFigureScalePolicy.MatchPreset(0.167f) == 6
            && VRFigureScalePolicy.MatchPreset(0.083f) == 12, "Rounded preset values still match");
        Check(VRFigureScalePolicy.MatchPreset(0.2f) == 0 && VRFigureScalePolicy.MatchPreset(float.NaN) == 0,
            "A scale between presets matches none");

        Check(VRFigureScalePolicy.Clamp(0.01f) == 0.05f && VRFigureScalePolicy.Clamp(5f) == 2f,
            "Scale is clamped to 0.05x..2x");
        Check(VRFigureScalePolicy.Clamp(float.NaN) == 1f && VRFigureScalePolicy.Clamp(float.PositiveInfinity) == 1f,
            "Invalid scale falls back to life size");

        Check(VRFigureScalePolicy.ToSlider(0.05f) == 0f && near(VRFigureScalePolicy.ToSlider(2f), 1f),
            "Slider ends are 0.05x and 2x");
        float previous = -1f;
        for (int i = VRFigureScalePolicy.PresetDenominators.Length - 1; i >= 0; i--)
        {
            float scale = VRFigureScalePolicy.PresetScale(VRFigureScalePolicy.PresetDenominators[i]);
            float fraction = VRFigureScalePolicy.ToSlider(scale);
            Check(fraction > previous, "Slider position grows with scale");
            Check(MathF.Abs(VRFigureScalePolicy.FromSlider(fraction) - scale) < scale * 1e-4f,
                "Slider round trip keeps preset 1/" + VRFigureScalePolicy.PresetDenominators[i]);
            previous = fraction;
        }
        Check(VRFigureScalePolicy.ToSlider(0.25f) - VRFigureScalePolicy.ToSlider(1f / 12f) > 0.25f,
            "The 1/12..1/4 figure band gets more than a quarter of the slider");
        Check(VRFigureScalePolicy.FromSlider(-1f) == 0.05f && near(VRFigureScalePolicy.FromSlider(3f), 2f),
            "Slider input outside the track is clamped");

        Check(VRFigureScalePolicy.SliderFraction(14f, 14f, 484f) == 0f
            && VRFigureScalePolicy.SliderFraction(498f, 14f, 484f) == 1f
            && near(VRFigureScalePolicy.SliderFraction(256f, 14f, 484f), 0.5f),
            "Pointer x maps linearly across the inset track");
        Check(VRFigureScalePolicy.SliderFraction(-50f, 14f, 484f) == 0f
            && VRFigureScalePolicy.SliderFraction(900f, 14f, 484f) == 1f
            && VRFigureScalePolicy.SliderFraction(100f, 14f, 0f) == 0f,
            "Pointer beyond the track or a zero-width track is clamped");

        float seventh = VRFigureScalePolicy.PresetScale(7);
        Check(near(VRFigureScalePolicy.Step(seventh, 1, false), seventh * 1.02f), "Fine step is +2%");
        Check(near(VRFigureScalePolicy.Step(seventh, -1, true), seventh / 1.1f), "Coarse step is -10%");
        Check(VRFigureScalePolicy.Step(2f, 1, true) == 2f && VRFigureScalePolicy.Step(0.05f, -1, true) == 0.05f,
            "Steps stop at the range ends");

        float current = 1f;
        float target = seventh;
        bool overshoot = false;
        int frames = 0;
        while (current != target && frames < 90)
        {
            float next = VRFigureScalePolicy.Approach(current, target, 1f / 90f);
            overshoot |= next < target;
            current = next;
            frames++;
        }
        Check(current == target && frames > 5, "Preset glides over several frames and lands exactly on 1/7");
        Check(frames < 90, "The glide settles within one second at 90 fps");
        Check(!overshoot, "The glide never overshoots below the target");
        Check(VRFigureScalePolicy.Approach(0.5f, 1f, 0f) == 0.5f, "A zero-length frame does not jump");
        Check(VRFigureScalePolicy.Approach(float.NaN, 0.25f, 0.01f) == 0.25f, "An invalid current scale snaps to the target");

        Check(VRFigureScalePolicy.FormatRatio(seventh) == "1/7", "1/7 label");
        Check(VRFigureScalePolicy.FormatRatio(VRFigureScalePolicy.PresetScale(12)) == "1/12", "1/12 label");
        Check(VRFigureScalePolicy.FormatRatio(1f) == "1.00x" && VRFigureScalePolicy.FormatRatio(1.5f) == "1.50x",
            "Life size and larger use an x multiplier");
        Check(VRFigureScalePolicy.FormatRatio(0.15f) == "1/6.7", "Between presets shows one decimal");
        Console.WriteLine("PASS: figure scale presets 1/1..1/12, log slider, steps, glide, and labels.");
    }

    private static void MmdPoseSanitize()
    {
        VRStudioInteractionPolicy.MmdPoseSanitizePlan ikPosed =
            VRStudioInteractionPolicy.PlanMmdPoseSanitize(true, false, 3, true);
        Check(ikPosed.DisableIk && !ikPosed.DisableFk && ikPosed.SettlePose && ikPosed.ReseatIkTargets,
            "IK-posed character: IK off, Animator pose settled before bind, IK targets re-seated");

        VRStudioInteractionPolicy.MmdPoseSanitizePlan fkPosed =
            VRStudioInteractionPolicy.PlanMmdPoseSanitize(false, true, 0, true);
        Check(!fkPosed.DisableIk && fkPosed.DisableFk && fkPosed.SettlePose && !fkPosed.ReseatIkTargets,
            "FK-posed character: FK off and settled; IK targets untouched");

        VRStudioInteractionPolicy.MmdPoseSanitizePlan both =
            VRStudioInteractionPolicy.PlanMmdPoseSanitize(true, true, 0, true);
        Check(both.DisableIk && both.DisableFk && both.SettlePose,
            "IK and FK both on (plugin mode): both switched off");

        VRStudioInteractionPolicy.MmdPoseSanitizePlan lockedOnly =
            VRStudioInteractionPolicy.PlanMmdPoseSanitize(false, false, 2, true);
        Check(!lockedOnly.DisableIk && !lockedOnly.DisableFk && lockedOnly.SettlePose && lockedOnly.ReseatIkTargets,
            "Released figure pose locks alone still settle the pose and re-seat the targets they moved");

        VRStudioInteractionPolicy.MmdPoseSanitizePlan clean =
            VRStudioInteractionPolicy.PlanMmdPoseSanitize(false, false, 0, true);
        Check(!clean.DisableIk && !clean.DisableFk && !clean.SettlePose && !clean.ReseatIkTargets,
            "A character with no posing state is left completely alone");

        VRStudioInteractionPolicy.MmdPoseSanitizePlan atPlay =
            VRStudioInteractionPolicy.PlanMmdPoseSanitize(true, false, 1, false);
        Check(atPlay.DisableIk && atPlay.ReseatIkTargets && !atPlay.SettlePose,
            "Playback start turns re-enabled IK off without re-evaluating the Animator mid-dance");

        string[] planFields = typeof(VRStudioInteractionPolicy.MmdPoseSanitizePlan)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Check(planFields.SequenceEqual(new[] { "DisableFk", "DisableIk", "ReseatIkTargets", "SettlePose" }),
            "Sanitize plan only carries IK/FK pose actions: " + string.Join(",", planFields));
        string[] forbidden = { "cloth", "coord", "accessor", "acc", "hair", "heel", "shoe", "shape", "body", "opacity", "scale" };
        Check(planFields.All(name => forbidden.All(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)),
            "Sanitize plan has no clothing, accessory, hair, heels, body-shape, or scale action");

        Check(VRStudioInteractionPolicy.ShouldSanitizeForVmdLoad(true)
            && !VRStudioInteractionPolicy.ShouldSanitizeForVmdLoad(false),
            "Only a motion VMD touches character poses; a camera-only VMD does not");
        Check(VRStudioInteractionPolicy.ShouldSanitizeBeforePlayback(false)
            && !VRStudioInteractionPolicy.ShouldSanitizeBeforePlayback(true),
            "Starting playback re-checks dancers; pausing does not");

        int[] live = { 3, 7, 11 };
        Check(VRStudioInteractionPolicy.ResolveMmdMotionTargets(new[] { 7, 7, 99 }, new[] { 3 }, live).SequenceEqual(new[] { 7 }),
            "Explicit targets win over the selection; dead and duplicate keys are dropped");
        Check(VRStudioInteractionPolicy.ResolveMmdMotionTargets(new int[0], new[] { 11, 3 }, live).SequenceEqual(new[] { 11, 3 }),
            "No explicit targets: the Studio selection is used");
        Check(VRStudioInteractionPolicy.ResolveMmdMotionTargets(null, null, live).Length == 0,
            "Nothing selected in a multi-character scene binds nobody");
        Check(VRStudioInteractionPolicy.ResolveMmdMotionTargets(null, new int[0], new[] { 5 }).SequenceEqual(new[] { 5 }),
            "A lone character is bound even when nothing is selected, like the MMDD script");
        Check(VRStudioInteractionPolicy.ResolveMmdMotionTargets(new[] { 42 }, null, new[] { 5 }).SequenceEqual(new[] { 5 }),
            "A stale explicit target in a one-character scene falls back to that character");
        Check(VRStudioInteractionPolicy.ResolveMmdMotionTargets(new[] { 1 }, new[] { 1 }, null).Length == 0,
            "No live characters binds nobody");
        Console.WriteLine("PASS: MMD pose sanitize clears IK/FK and pose locks only, settles before bind, and targets the MMDD actors.");
    }
}
