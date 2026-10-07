using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace KKCharaStudioVR;

// Shared feature code keeps its legacy call shape. Reads go through the
// SteamVR legacy_emulate actions, with Quest Touch paths mapped to the same
// EVRButtonId values the old OpenVR driver reported.
public static class SteamVR_Controller
{
    public const int UnpublishedLeftIndex = 126;
    public const int UnpublishedRightIndex = 127;
    public const string ActionMapSummary =
        "A_Press = k_EButton_A (Right A, Left X); " +
        "ApplicationMenu_Press = k_EButton_ApplicationMenu (Right B, Left Y); " +
        "Axis0_Press = k_EButton_Axis0 stick click (k_EButton_SteamVR_Touchpad); " +
        "Axis0_2D = joystick; Axis1_Press + Axis1_1D = k_EButton_Axis1 trigger; " +
        "Grip_Press + Grip_1D = k_EButton_Grip; sources = LeftHand/RightHand; " +
        "action set = /actions/legacy_emulate.";

    public static bool LogButtonEdges = true;
    public static Action<string> EdgeLog;

    static readonly Dictionary<SteamVR_Behaviour_Pose, Device> Devices = new();
    static readonly Dictionary<int, SteamVR_Behaviour_Pose> ByIndex = new();
    static readonly Dictionary<int, bool> EdgeLevel = new();

    public readonly struct OculusBinding
    {
        public readonly string Path;
        public readonly string Mode;
        public readonly string Click;
        public readonly string Touch;
        public readonly string Pull;
        public readonly string Position;
        public OculusBinding(string path, string mode, string click = null, string touch = null, string pull = null, string position = null)
        {
            Path = path;
            Mode = mode;
            Click = click;
            Touch = touch;
            Pull = pull;
            Position = position;
        }
    }

    // Physical Quest paths. Right X/Y and Left A/B are not oculus_touch inputs.
    public static readonly OculusBinding[] OculusBindings =
    {
        new("/user/hand/left/input/x", "button", click: "a_press", touch: "a_touch"),
        new("/user/hand/right/input/a", "button", click: "a_press", touch: "a_touch"),
        new("/user/hand/left/input/y", "button", click: "applicationmenu_press", touch: "applicationmenu_touch"),
        new("/user/hand/right/input/b", "button", click: "applicationmenu_press", touch: "applicationmenu_touch"),
        new("/user/hand/left/input/joystick", "joystick", click: "axis0_press", touch: "axis0_touch", position: "axis0_2d"),
        new("/user/hand/right/input/joystick", "joystick", click: "axis0_press", touch: "axis0_touch", position: "axis0_2d"),
        new("/user/hand/left/input/trigger", "trigger", click: "axis1_press", touch: "axis1_touch", pull: "axis1_1d"),
        new("/user/hand/right/input/trigger", "trigger", click: "axis1_press", touch: "axis1_touch", pull: "axis1_1d"),
        new("/user/hand/left/input/grip", "trigger", click: "grip_press", touch: "grip_touch", pull: "grip_1d"),
        new("/user/hand/right/input/grip", "trigger", click: "grip_press", touch: "grip_touch", pull: "grip_1d"),
        new("/user/hand/left/input/system", "button", click: "system_press"),
    };

    public static bool SuppressSingleToolCycle(int toolCount) => toolCount <= 1;

    public static int ResolveTrackedIndex(int deviceIndex, SteamVR_Input_Sources source, bool trackingActive)
    {
        if (deviceIndex >= 0) return deviceIndex;
        if (!trackingActive) return -1;
        if (source == SteamVR_Input_Sources.LeftHand) return UnpublishedLeftIndex;
        if (source == SteamVR_Input_Sources.RightHand) return UnpublishedRightIndex;
        return -1;
    }

    public static void Remember(int index, SteamVR_Behaviour_Pose pose)
    {
        if (pose == null) return;
        var stale = new List<int>();
        foreach (var pair in ByIndex)
            if (pair.Value == pose && pair.Key != index) stale.Add(pair.Key);
        foreach (int key in stale) ByIndex.Remove(key);
        if (index >= 0) ByIndex[index] = pose;
    }

    public static string OculusOutput(string path, string input)
    {
        foreach (var source in OculusBindings)
        {
            if (source.Path != path) continue;
            string name = input == "click" ? source.Click
                : input == "touch" ? source.Touch
                : input == "pull" ? source.Pull
                : input == "position" ? source.Position
                : null;
            return string.IsNullOrEmpty(name) ? null : "/actions/legacy_emulate/in/" + name;
        }
        return null;
    }

    public static string BuildOculusTouchBindingJson()
    {
        var sources = new StringBuilder();
        for (int i = 0; i < OculusBindings.Length; i++)
        {
            if (i > 0) sources.Append(",\n");
            sources.Append(SourceJson(OculusBindings[i]));
        }
        return "{\n" +
            "   \"action_manifest_version\" : 0,\n" +
            "   \"alias_info\" : {},\n" +
            "   \"app_key\" : \"system.generated.charastudio.exe\",\n" +
            "   \"bindings\" : {\n" +
            "      \"/actions/legacy_emulate\" : {\n" +
            "         \"haptics\" : [\n" +
            "            {\n" +
            "               \"output\" : \"/actions/legacy_emulate/out/huptic\",\n" +
            "               \"path\" : \"/user/hand/left/output/haptic\"\n" +
            "            },\n" +
            "            {\n" +
            "               \"output\" : \"/actions/legacy_emulate/out/huptic\",\n" +
            "               \"path\" : \"/user/hand/right/output/haptic\"\n" +
            "            }\n" +
            "         ],\n" +
            "         \"poses\" : [\n" +
            "            {\n" +
            "               \"output\" : \"/actions/legacy_emulate/in/pose\",\n" +
            "               \"path\" : \"/user/hand/left/pose/raw\"\n" +
            "            },\n" +
            "            {\n" +
            "               \"output\" : \"/actions/legacy_emulate/in/pose\",\n" +
            "               \"path\" : \"/user/hand/right/pose/raw\"\n" +
            "            }\n" +
            "         ],\n" +
            "         \"sources\" : [\n" +
            sources.ToString() +
            "\n         ]\n" +
            "      }\n" +
            "   },\n" +
            "   \"category\" : \"steamvr_input\",\n" +
            "   \"controller_type\" : \"oculus_touch\",\n" +
            "   \"description\" : \"KK legacy EVRButtonId map. Right A and Left X are A_Press. Right B and Left Y are ApplicationMenu. Joystick is Axis0. Trigger is Axis1. Grip is Grip.\",\n" +
            "   \"name\" : \"kks_legacy_oculus_touch\",\n" +
            "   \"options\" : {},\n" +
            "   \"simulated_actions\" : []\n" +
            "}\n";
    }

    public static Device Input(int index)
    {
        if (index < 0 || !VR.Active || VR.Mode == null) return null;
        Controller found = Find(VR.Mode.Left, index) ?? Find(VR.Mode.Right, index);
        if (found == null)
            found = FindUnpublished(VR.Mode.Left, index) ?? FindUnpublished(VR.Mode.Right, index);
        if (found == null && ByIndex.TryGetValue(index, out SteamVR_Behaviour_Pose remembered) && remembered != null
            && (VR.Mode.Left?.Tracking == remembered || VR.Mode.Right?.Tracking == remembered))
        {
            int published = SafeDeviceIndex(remembered);
            if (published < 0 || published == index)
                return ForPose(remembered);
            ByIndex.Remove(index);
        }
        if (found == null) return null;
        Remember(index, found.Tracking);
        return ForPose(found.Tracking);
    }

    public static Device ForController(Controller controller)
    {
        if (controller == null || controller.Tracking == null || !VR.Active || VR.Mode == null)
            return null;
        int index = SafeDeviceIndex(controller.Tracking);
        if (index >= 0)
        {
            Remember(index, controller.Tracking);
            Device indexed = Input(index);
            if (indexed != null)
                return indexed;
        }
        // The pose can already deliver buttons before its device index is published.
        return ForPose(controller.Tracking);
    }

    static Device ForPose(SteamVR_Behaviour_Pose pose)
    {
        if (pose == null) return null;
        if (!Devices.TryGetValue(pose, out Device device))
        {
            foreach (var stale in new List<SteamVR_Behaviour_Pose>(Devices.Keys))
                if (stale == null) Devices.Remove(stale);
            Devices[pose] = device = new Device(pose);
        }
        return device;
    }

    static Controller Find(Controller controller, int index) =>
        controller != null && controller.Tracking != null && SafeDeviceIndex(controller.Tracking) == index
            ? controller : null;

    static Controller FindUnpublished(Controller controller, int index)
    {
        if (controller?.Tracking == null || SafeDeviceIndex(controller.Tracking) >= 0) return null;
        int resolved = ResolveTrackedIndex(-1, controller.Tracking.inputSource, true);
        return resolved == index ? controller : null;
    }

    static int SafeDeviceIndex(SteamVR_Behaviour_Pose pose)
    {
        try { return pose.GetDeviceIndex(); }
        catch { return -1; }
    }

    static string SourceJson(OculusBinding source)
    {
        var inputs = new StringBuilder();
        AppendInput(inputs, "click", source.Click);
        AppendInput(inputs, "touch", source.Touch);
        AppendInput(inputs, "pull", source.Pull);
        AppendInput(inputs, "position", source.Position);
        return "            {\n" +
            "               \"inputs\" : {\n" +
            inputs +
            "\n               },\n" +
            "               \"mode\" : \"" + source.Mode + "\",\n" +
            "               \"path\" : \"" + source.Path + "\"\n" +
            "            }";
    }

    static void AppendInput(StringBuilder inputs, string slot, string action)
    {
        if (string.IsNullOrEmpty(action)) return;
        if (inputs.Length > 0) inputs.Append(",\n");
        inputs.Append("                  \"").Append(slot).Append("\" : {\n");
        inputs.Append("                     \"output\" : \"/actions/legacy_emulate/in/").Append(action).Append("\"\n");
        inputs.Append("                  }");
    }

    public sealed class Device : DeviceLegacyAdapter
    {
        readonly SteamVR_Behaviour_Pose pose;
        static readonly EVRButtonId[] Buttons =
        {
            EVRButtonId.k_EButton_System, EVRButtonId.k_EButton_ApplicationMenu,
            EVRButtonId.k_EButton_Grip, EVRButtonId.k_EButton_A,
            EVRButtonId.k_EButton_Axis0, EVRButtonId.k_EButton_Axis1,
            EVRButtonId.k_EButton_Axis2, EVRButtonId.k_EButton_Axis3, EVRButtonId.k_EButton_Axis4
        };

        public Device(SteamVR_Behaviour_Pose pose) : base(pose) => this.pose = pose;

        // deviceIsConnected is the hardware flag. isValid / isActive / tracking
        // state flicker on OpenXR and must not zero button or axis reads.
        public bool connected => pose != null && pose.poseAction != null && DeviceIsConnected();
        public bool hasTracking => connected && SafeFlag(() => pose.isActive) && SafeFlag(() => pose.isValid);
        public bool outOfRange => connected && TrackingState() == ETrackingResult.Running_OutOfRange;
        public bool calibrating => connected && (TrackingState() == ETrackingResult.Calibrating_InProgress || TrackingState() == ETrackingResult.Calibrating_OutOfRange);
        public bool uninitialized => !connected || TrackingState() == ETrackingResult.Uninitialized;

        public new bool GetPress(EVRButtonId button)
        {
            EnsureHandSource();
            if (!connected) return false;
            var action = GetActionBoolean_Press(button);
            return action != null && action.state;
        }
        public new bool GetPressDown(EVRButtonId button)
        {
            EnsureHandSource();
            bool down = false;
            if (connected)
            {
                var action = GetActionBoolean_Press(button);
                down = action != null && action.stateDown;
            }
            NoteEdge(button, true, down);
            return down;
        }
        public new bool GetPressUp(EVRButtonId button)
        {
            EnsureHandSource();
            bool up = false;
            if (connected)
            {
                var action = GetActionBoolean_Press(button);
                up = action != null && action.stateUp;
            }
            NoteEdge(button, false, up);
            return up;
        }
        public new bool GetTouch(EVRButtonId button)
        {
            EnsureHandSource();
            if (!connected) return false;
            var action = GetActionBoolean_Touch(button);
            return action != null && action.state;
        }
        public new bool GetTouchDown(EVRButtonId button)
        {
            EnsureHandSource();
            if (!connected) return false;
            var action = GetActionBoolean_Touch(button);
            return action != null && action.stateDown;
        }
        public new bool GetTouchUp(EVRButtonId button)
        {
            EnsureHandSource();
            if (!connected) return false;
            var action = GetActionBoolean_Touch(button);
            return action != null && action.stateUp;
        }

        bool ReadMask(ulong mask, bool touch, int edge)
        {
            if (!connected) return false;
            bool current = false, previous = false;
            foreach (var button in Buttons)
            {
                if ((mask & (1UL << (int)button)) == 0) continue;
                var action = touch ? GetActionBoolean_Touch(button) : GetActionBoolean_Press(button);
                if (action == null) continue;
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
            EnsureHandSource();
            if (!connected) return Vector2.zero;
            var actions = SteamVR_Actions.legacy_emulate;
            var source = pose.inputSource;
            // The stock adapter reads Axis1_2D / Grip_2D, which oculus_touch does not bind.
            // Trigger pull and grip pull are the scalar actions in the binding.
            if (button == EVRButtonId.k_EButton_Axis1)
                return new Vector2(actions.Axis1_1D[source].axis, 0);
            if (button == EVRButtonId.k_EButton_Axis2)
                return new Vector2(actions.Grip_1D[source].axis, 0);
            if (button == EVRButtonId.k_EButton_Axis0)
                return actions.Axis0_2D[source].axis;
            return base.GetAxis(button);
        }

        public new void TriggerHapticPulse(ushort durationMicroSec = 500, EVRButtonId button = EVRButtonId.k_EButton_Axis0)
        {
            EnsureHandSource();
            if (!connected || VR.Settings == null || !VR.Settings.Rumble) return;
            SteamVR_Actions.legacy_emulate.Huptic[pose.inputSource].Execute(0, durationMicroSec / 1000000f, 100, 1);
        }

        bool DeviceIsConnected()
        {
            try { return pose.poseAction[pose.inputSource].deviceIsConnected; }
            catch { return false; }
        }

        ETrackingResult TrackingState()
        {
            try { return pose.poseAction[pose.inputSource].trackingState; }
            catch { return ETrackingResult.Uninitialized; }
        }

        static bool SafeFlag(Func<bool> read)
        {
            try { return read(); }
            catch { return false; }
        }

        void EnsureHandSource()
        {
            if (pose == null || pose.inputSource != SteamVR_Input_Sources.Any || VR.Mode == null) return;
            if (VR.Mode.Left?.Tracking == pose) pose.inputSource = SteamVR_Input_Sources.LeftHand;
            else if (VR.Mode.Right?.Tracking == pose) pose.inputSource = SteamVR_Input_Sources.RightHand;
        }

        void NoteEdge(EVRButtonId button, bool isDown, bool active)
        {
            if (!LogButtonEdges || pose == null) return;
            string name = EdgeName(button);
            if (name == null) return;
            int id = ((int)pose.inputSource << 16) | (int)button | (isDown ? 0 : 0x100);
            bool was = EdgeLevel.TryGetValue(id, out bool level) && level;
            EdgeLevel[id] = active;
            if (!active || was) return;
            string hand = pose.inputSource == SteamVR_Input_Sources.LeftHand ? "L"
                : pose.inputSource == SteamVR_Input_Sources.RightHand ? "R"
                : pose.inputSource.ToString();
            EdgeLog?.Invoke("[KKS VR Input] " + hand + " " + name + (isDown ? " down" : " up"));
        }

        static string EdgeName(EVRButtonId button)
        {
            if (button == EVRButtonId.k_EButton_A) return "A";
            if (button == EVRButtonId.k_EButton_ApplicationMenu) return "AppMenu";
            if (button == EVRButtonId.k_EButton_Grip) return "Grip";
            if (button == EVRButtonId.k_EButton_Axis1) return "Trigger";
            if (button == EVRButtonId.k_EButton_Axis0) return "StickClick";
            return null;
        }
    }
}
