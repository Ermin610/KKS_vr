using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;
using VRGIN.Modes;

namespace KKCharaStudioVR;

// Installs the Quest legacy binding before SteamVR reads the manifest, keeps
// the single legacy action set active, and stops VRGIN from cycling the only tool.
internal static class KksInputStartup
{
    static bool installed;

    public static void InstallBeforeRuntime()
    {
        SteamVR_Controller.EdgeLog = message => VRLog.Info(message);
        try
        {
            var config = new ConfigFile(Path.Combine(Paths.ConfigPath, "KKS_VR_Input.cfg"), true);
            SteamVR_Controller.LogButtonEdges = config.Bind(
                "Input",
                "LogButtonEdges",
                true,
                "Log one [KKS VR Input] line per A, AppMenu, Grip, Trigger, and StickClick edge.").Value;
        }
        catch (Exception ex)
        {
            VRLog.Warn("[KKS VR Input] edge-log config unavailable; logging stays on: " + ex.Message);
        }
        InstallToolGuard();
        InstallOculusBinding();
    }

    public static void ActivateActionSet()
    {
        try
        {
            SteamVR_Input_ActionSet_legacy_emulate set = SteamVR_Actions.legacy_emulate;
            bool was = set != null && set.IsActive();
            if (set != null)
                set.Activate(SteamVR_Input_Sources.Any, 1, false);
            VRLog.Info("[KKS VR Input] " + SteamVR_Controller.ActionMapSummary);
            VRLog.Info("[KKS VR Input] legacy_emulate active before=" + was
                + " after=" + (set != null && set.IsActive(SteamVR_Input_Sources.Any)));
        }
        catch (Exception ex)
        {
            VRLog.Error("[KKS VR Input] action set activation failed: " + ex);
        }
    }

    public static void LogHands()
    {
        LogHand("L", VR.Mode == null ? null : VR.Mode.Left);
        LogHand("R", VR.Mode == null ? null : VR.Mode.Right);
    }

    static void InstallToolGuard()
    {
        if (installed)
            return;
        installed = true;
        new Harmony("KKCharaStudioVR.Input").PatchAll(typeof(ToolCyclePatch));
        new Harmony("KKCharaStudioVR.InputSources").PatchAll(typeof(ControllerSourcePatch));
    }

    static void InstallOculusBinding()
    {
        try
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "SteamVR");
            string path = Path.Combine(folder, "bindings_oculus_touch.json");
            string desired = SteamVR_Controller.BuildOculusTouchBindingJson();
            string current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (Norm(current) == Norm(desired))
            {
                VRLog.Info("[KKS VR Input] oculus_touch binding already matches the legacy EVRButtonId map.");
                return;
            }
            Directory.CreateDirectory(folder);
            string backup = path + ".pre-kks-legacy.bak";
            if (current != null && !File.Exists(backup))
                File.Copy(path, backup, false);
            File.WriteAllText(path, desired);
            VRLog.Info("[KKS VR Input] rewrote bindings_oculus_touch.json: X/A→A_Press, Y/B→ApplicationMenu, stick→Axis0, trigger→Axis1, grip→Grip.");
        }
        catch (Exception ex)
        {
            VRLog.Error("[KKS VR Input] failed to install oculus_touch binding: " + ex.Message);
        }
    }

    static void LogHand(string side, Controller controller)
    {
        SteamVR_Behaviour_Pose pose = controller == null ? null : controller.Tracking;
        if (pose == null)
        {
            VRLog.Info("[KKS VR Input] " + side + " pose missing");
            return;
        }
        int index = -1;
        bool active = false, valid = false, connected = false;
        try { index = pose.GetDeviceIndex(); } catch { }
        try { active = pose.isActive; valid = pose.isValid; } catch { }
        try { connected = pose.poseAction != null && pose.poseAction[pose.inputSource].deviceIsConnected; } catch { }
        VRLog.Info("[KKS VR Input] " + side + " inputSource=" + pose.inputSource
            + " deviceIndex=" + index + " connected=" + connected
            + " active=" + active + " valid=" + valid);
    }

    static string Norm(string text) => text == null ? null : text.Replace("\r\n", "\n").Trim();

    [HarmonyPatch(typeof(Controller), "OnUpdate")]
    static class ToolCyclePatch
    {
        static readonly FieldInfo LockField = AccessTools.Field(typeof(Controller), "_Lock");
        static readonly MethodInfo TryRelease = AccessTools.Method(typeof(Controller), "TryReleaseLock");
        static bool logged;

        static bool Prefix(Controller __instance)
        {
            int count = __instance == null || __instance.Tools == null ? 0 : __instance.Tools.Count;
            if (!SteamVR_Controller.SuppressSingleToolCycle(count))
                return true;
            try
            {
                if (!logged && __instance.Tracking != null && __instance.Input != null
                    && __instance.Input.GetPressUp(EVRButtonId.k_EButton_ApplicationMenu))
                {
                    logged = true;
                    VRLog.Info("[KKS VR Input] single-tool mode ignores ApplicationMenu press-up tool cycling; B/Y long-press reset stays on the grip tool.");
                }
                if (__instance.Tracking != null && LockField != null)
                {
                    var lockObj = LockField.GetValue(__instance) as Controller.Lock;
                    if (lockObj != null && lockObj.IsInvalidating && TryRelease != null)
                        TryRelease.Invoke(__instance, null);
                }
            }
            catch (Exception ex)
            {
                VRLog.Error("[KKS VR Input] tool-cycle guard failed: " + ex.Message);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ControlMode), "CreateControllers")]
    static class ControllerSourcePatch
    {
        static void Postfix(ControlMode __instance)
        {
            try
            {
                EnsureSource(__instance.Left, SteamVR_Input_Sources.LeftHand);
                EnsureSource(__instance.Right, SteamVR_Input_Sources.RightHand);
                if (__instance.Left != null)
                    KksTrackedObject.AttachHierarchy(((Component)__instance.Left).gameObject);
                if (__instance.Right != null)
                    KksTrackedObject.AttachHierarchy(((Component)__instance.Right).gameObject);
            }
            catch (Exception ex)
            {
                VRLog.Error("[KKS VR Input] controller post-setup failed: " + ex.Message);
            }
        }

        static void EnsureSource(Controller controller, SteamVR_Input_Sources hand)
        {
            if (controller == null || controller.Tracking == null || controller.Tracking.inputSource == hand)
                return;
            SteamVR_Input_Sources previous = controller.Tracking.inputSource;
            controller.Tracking.enabled = false;
            controller.InputSources = hand;
            controller.Tracking.enabled = true;
            VRLog.Info("[KKS VR Input] corrected " + hand + " pose inputSource from " + previous);
        }
    }
}
