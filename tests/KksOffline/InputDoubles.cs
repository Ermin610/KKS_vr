using UnityEngine;

namespace Valve.VR
{
    public enum EVRButtonId { k_EButton_System, k_EButton_ApplicationMenu, k_EButton_Grip, k_EButton_A = 7, k_EButton_Axis0 = 32, k_EButton_Axis1, k_EButton_Axis2, k_EButton_Axis3, k_EButton_Axis4, k_EButton_SteamVR_Touchpad = 32, k_EButton_SteamVR_Trigger = 33 }
    public enum ETrackingResult { Uninitialized, Running_OK, Running_OutOfRange, Calibrating_InProgress, Calibrating_OutOfRange }
    public enum SteamVR_Input_Sources { Any, LeftHand, RightHand }
    public sealed class BooleanAction { public bool state, stateDown, stateUp; }
    public sealed class PoseSource { public bool deviceIsConnected = true; public ETrackingResult trackingState = ETrackingResult.Running_OK; }
    public sealed class PoseAction
    {
        public PoseSource source = new();
        public PoseSource this[SteamVR_Input_Sources input] => source;
    }
    public sealed class SteamVR_Behaviour_Pose
    {
        public PoseAction poseAction = new();
        public SteamVR_Input_Sources inputSource;
        public bool isActive = true, isValid = true;
        public int index;
        public int GetDeviceIndex() => index;
        public readonly Dictionary<EVRButtonId, BooleanAction> press = new(), touch = new();
    }
    public sealed class AxisAction
    {
        public float axis;
        public AxisAction this[SteamVR_Input_Sources source] => this;
    }
    public sealed class HapticAction
    {
        public int calls;
        public float duration;
        public SteamVR_Input_Sources lastSource;
        public HapticAction this[SteamVR_Input_Sources source] { get { lastSource = source; return this; } }
        public void Execute(float delay, float seconds, float frequency, float amplitude) { calls++; duration = seconds; }
    }
    public sealed class Vector2Action
    {
        public Vector2 axis;
        public Vector2Action this[SteamVR_Input_Sources source] => this;
    }
    public sealed class LegacyActions
    {
        public AxisAction Axis1_1D = new(), Grip_1D = new();
        public Vector2Action Axis0_2D = new();
        public HapticAction Huptic = new();
    }
    public static class SteamVR_Actions { public static LegacyActions legacy_emulate = new(); }
}
namespace VRGIN.Controls
{
    using Valve.VR;
    public class Controller { public SteamVR_Behaviour_Pose Tracking; }
    public class DeviceLegacyAdapter
    {
        private readonly SteamVR_Behaviour_Pose pose;
        public DeviceLegacyAdapter(SteamVR_Behaviour_Pose pose) { this.pose = pose; }
        public BooleanAction GetActionBoolean_Press(EVRButtonId id) => Get(pose.press, id);
        public BooleanAction GetActionBoolean_Touch(EVRButtonId id) => Get(pose.touch, id);
        private static BooleanAction Get(Dictionary<EVRButtonId, BooleanAction> map, EVRButtonId id)
        {
            if (!map.TryGetValue(id, out var a)) map[id] = a = new();
            return a;
        }
        // These base calls must never handle the compatibility layer's public
        // button APIs. Throwing detects accidental inherited dispatch.
        public bool GetPress(EVRButtonId id) => throw new Exception("Inherited press");
        public bool GetPressDown(EVRButtonId id) => throw new Exception("Inherited press down");
        public bool GetPressUp(EVRButtonId id) => throw new Exception("Inherited press up");
        public bool GetTouch(EVRButtonId id) => throw new Exception("Inherited touch");
        public bool GetTouchDown(EVRButtonId id) => throw new Exception("Inherited touch down");
        public bool GetTouchUp(EVRButtonId id) => throw new Exception("Inherited touch up");
        public bool GetPress(ulong id) => throw new Exception("Inherited mask");
        public bool GetPressDown(ulong id) => throw new Exception("Inherited mask");
        public bool GetPressUp(ulong id) => throw new Exception("Inherited mask");
        public bool GetTouch(ulong id) => throw new Exception("Inherited mask");
        public bool GetTouchDown(ulong id) => throw new Exception("Inherited mask");
        public bool GetTouchUp(ulong id) => throw new Exception("Inherited mask");
        public Vector2 GetAxis(EVRButtonId id = EVRButtonId.k_EButton_Axis0) => new(.25f, -.5f);
        public void TriggerHapticPulse(ushort duration = 500, EVRButtonId id = EVRButtonId.k_EButton_Axis0) => throw new Exception("Inherited haptics");
    }
}
namespace VRGIN.Core
{
    public sealed class Mode { public VRGIN.Controls.Controller Left, Right; }
    public sealed class Settings { public bool Rumble = true; }
    public static class VR { public static bool Active = true; public static Mode Mode; public static Settings Settings = new(); }
}
