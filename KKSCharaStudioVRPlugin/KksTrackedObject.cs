using UnityEngine;
using Valve.VR;
using VRGIN.Controls;

namespace KKCharaStudioVR;

// A read-only view of the KKS pose. Never writes transforms or subscribes a
// second pose driver; SteamVR_Behaviour_Pose remains the tracking owner.
public sealed class KksTrackedObject : MonoBehaviour
{
    public enum EIndex { None = -1, Hmd = 0 }
    static bool attaching;
    SteamVR_Behaviour_Pose pose;

    public EIndex index
    {
        get
        {
            EnsurePose();
            if (pose == null)
                return EIndex.None;

            int deviceIndex = -1;
            try { deviceIndex = pose.GetDeviceIndex(); }
            catch { deviceIndex = -1; }
            if (deviceIndex < 0)
                deviceIndex = RoleIndex();

            bool tracking = deviceIndex >= 0 || IsTrackingActive();
            int resolved = SteamVR_Controller.ResolveTrackedIndex(deviceIndex, pose.inputSource, tracking);
            if (resolved < 0)
                return EIndex.None;
            SteamVR_Controller.Remember(resolved, pose);
            return (EIndex)resolved;
        }
    }

    public bool isValid
    {
        get
        {
            EnsurePose();
            if (pose == null || index == EIndex.None)
                return false;
            try
            {
                if (pose.isValid)
                    return true;
                return pose.poseAction != null && pose.poseAction[pose.inputSource].deviceIsConnected;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void AttachHierarchy(GameObject root)
    {
        if (root == null || attaching)
            return;
        attaching = true;
        try { AttachRecursive(root); }
        finally { attaching = false; }
    }

    private void Awake() => EnsurePose();

    private void OnTransformChildrenChanged()
    {
        if (!attaching)
            AttachHierarchy(gameObject);
    }

    static void AttachRecursive(GameObject root)
    {
        if (root.GetComponent<KksTrackedObject>() == null)
            root.AddComponent<KksTrackedObject>();
        int count = root.transform.childCount;
        for (int i = 0; i < count; i++)
            AttachRecursive(root.transform.GetChild(i).gameObject);
    }

    private void EnsurePose()
    {
        if (pose != null && (pose.gameObject == gameObject || transform.IsChildOf(pose.transform)))
            return;

        pose = GetComponent<SteamVR_Behaviour_Pose>()
            ?? GetComponentInParent<SteamVR_Behaviour_Pose>()
            ?? GetComponentInChildren<SteamVR_Behaviour_Pose>(true);
        if (pose != null)
            return;

        Controller controller = GetComponent<Controller>()
            ?? GetComponentInParent<Controller>();
        if (controller != null)
            pose = controller.Tracking;
    }

    private int RoleIndex()
    {
        try
        {
            if (pose.inputSource != SteamVR_Input_Sources.LeftHand
                && pose.inputSource != SteamVR_Input_Sources.RightHand)
                return -1;
            var system = OpenVR.System;
            if (system == null)
                return -1;
            uint device = system.GetTrackedDeviceIndexForControllerRole(
                pose.inputSource == SteamVR_Input_Sources.LeftHand
                    ? ETrackedControllerRole.LeftHand
                    : ETrackedControllerRole.RightHand);
            if (device == 0 || device == uint.MaxValue || device > 63)
                return -1;
            return (int)device;
        }
        catch
        {
            return -1;
        }
    }

    private bool IsTrackingActive()
    {
        try
        {
            if (pose.isActive || pose.isValid)
                return true;
            return pose.poseAction != null && pose.poseAction[pose.inputSource].deviceIsConnected;
        }
        catch
        {
            return false;
        }
    }
}
