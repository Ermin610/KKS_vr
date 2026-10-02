using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace KKCharaStudioVR;

// Shared feature code keeps its legacy call shape, while all input is read
// from KKS's SteamVR actions and the current pose's physical device index.
public static class SteamVR_Controller
{
    private static readonly Dictionary<SteamVR_Behaviour_Pose, Device> Devices = new();

    public static Device Input(int index)
    {
        if (index < 0 || !VR.Active || VR.Mode == null) return null;
        Controller controller = Find(VR.Mode.Left, index) ?? Find(VR.Mode.Right, index);
        if (controller == null) return null;
        var pose = controller.Tracking;
        if (!Devices.TryGetValue(pose, out Device device))
        {
            foreach (var stale in new List<SteamVR_Behaviour_Pose>(Devices.Keys))
                if (stale == null) Devices.Remove(stale);
            Devices[pose] = device = new Device(pose);
        }
        return device;
    }

    private static Controller Find(Controller controller, int index) =>
        controller != null && controller.Tracking != null && controller.Tracking.GetDeviceIndex() == index
            ? controller : null;

    public sealed class Device : DeviceLegacyAdapter
    {
        private readonly SteamVR_Behaviour_Pose pose;
        public Device(SteamVR_Behaviour_Pose pose) : base(pose) => this.pose = pose;
        public bool connected => pose != null && pose.poseAction != null && pose.isActive && pose.poseAction[pose.inputSource].deviceIsConnected;
        public bool hasTracking => connected && pose.isValid;
        public bool outOfRange => connected && pose.poseAction[pose.inputSource].trackingState == ETrackingResult.Running_OutOfRange;
        public bool calibrating => connected && (pose.poseAction[pose.inputSource].trackingState == ETrackingResult.Calibrating_InProgress || pose.poseAction[pose.inputSource].trackingState == ETrackingResult.Calibrating_OutOfRange);
        public bool uninitialized => !connected || pose.poseAction[pose.inputSource].trackingState == ETrackingResult.Uninitialized;

        // The runtime adapter confuses touch-up with press-up, and some mask
        // overloads read the wrong edge. Keep all legacy calls on one mapping.
        public new bool GetPress(EVRButtonId button) => connected && GetActionBoolean_Press(button).state;
        public new bool GetPressDown(EVRButtonId button) => connected && GetActionBoolean_Press(button).stateDown;
        public new bool GetPressUp(EVRButtonId button) => connected && GetActionBoolean_Press(button).stateUp;
        public new bool GetTouch(EVRButtonId button) => connected && GetActionBoolean_Touch(button).state;
        public new bool GetTouchDown(EVRButtonId button) => connected && GetActionBoolean_Touch(button).stateDown;
        public new bool GetTouchUp(EVRButtonId button) => connected && GetActionBoolean_Touch(button).stateUp;

        private static readonly EVRButtonId[] Buttons = {
            EVRButtonId.k_EButton_System, EVRButtonId.k_EButton_ApplicationMenu,
            EVRButtonId.k_EButton_Grip, EVRButtonId.k_EButton_A,
            EVRButtonId.k_EButton_Axis0, EVRButtonId.k_EButton_Axis1,
            EVRButtonId.k_EButton_Axis2, EVRButtonId.k_EButton_Axis3, EVRButtonId.k_EButton_Axis4
        };
        private bool ReadMask(ulong mask, bool touch, int edge)
        {
            if (!connected) return false;
            bool current = false, previous = false;
            foreach (var button in Buttons)
            {
                if ((mask & (1UL << (int)button)) == 0) continue;
                var action = touch ? GetActionBoolean_Touch(button) : GetActionBoolean_Press(button);
                current |= action.state;
                previous |= action.stateUp || (action.state && !action.stateDown);
            }
            return edge == 0 ? current : edge > 0 ? current && !previous : previous && !current;
        }
        public new bool GetPress(ulong mask) => ReadMask(mask, false, 0);
        public new bool GetPressDown(ulong mask) => ReadMask(mask, false, 1);
        public new bool GetPressUp(ulong mask) => ReadMask(mask, false, -1);
        public new bool GetTouch(ulong mask) => ReadMask(mask, true, 0);
        public new bool GetTouchDown(ulong mask) => ReadMask(mask, true, 1);
        public new bool GetTouchUp(ulong mask) => ReadMask(mask, true, -1);
        public new Vector2 GetAxis(EVRButtonId button = EVRButtonId.k_EButton_Axis0)
        {
            if (!connected) return Vector2.zero;
            var actions = SteamVR_Actions.legacy_emulate;
            if (button == EVRButtonId.k_EButton_Axis1)
                return new Vector2(actions.Axis1_1D[pose.inputSource].axis, 0);
            if (button == EVRButtonId.k_EButton_Axis2)
                return new Vector2(actions.Grip_1D[pose.inputSource].axis, 0);
            return base.GetAxis(button);
        }
        public new void TriggerHapticPulse(ushort durationMicroSec = 500, EVRButtonId button = EVRButtonId.k_EButton_Axis0)
        {
            if (connected && VR.Settings.Rumble)
                SteamVR_Actions.legacy_emulate.Huptic[pose.inputSource].Execute(0, durationMicroSec / 1000000f, 100, 1);
        }
    }
}
