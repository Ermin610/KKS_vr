using KKCharaStudioVR;
using KK_VR_CameraSync;
using UnityEngine;
using Valve.VR;

static class Program
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
        Input();
        Timeline();
        Console.WriteLine($"PASS: {checks} offline assertions against linked production input/Timeline code.");
        Console.WriteLine("Unity math and SteamVR/game services are doubles; no headset, game process or installation was used.");
    }
    private static void Input()
    {
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
        Check(device.GetAxis(EVRButtonId.k_EButton_Axis1).x == .8f, "Trigger uses scalar action");
        Check(device.GetAxis(EVRButtonId.k_EButton_Axis2).x == .6f, "Grip uses scalar action");
        Check(device.GetAxis().y == -.5f, "Stick retains both axes");
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
        Console.WriteLine("PASS: input edges, masks, analog mapping, disconnects, device binding and haptics.");
    }
    private static void Timeline()
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
        Console.WriteLine("PASS: Timeline pause/resume, source/rig changes, offsets, suppression and camera ownership.");
    }
}
