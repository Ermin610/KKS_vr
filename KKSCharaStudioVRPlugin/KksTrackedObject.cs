using UnityEngine;
using Valve.VR;

namespace KKCharaStudioVR;

// A read-only view of the KKS pose. Never writes transforms or subscribes a
// second pose driver; SteamVR_Behaviour_Pose remains the tracking owner.
public sealed class KksTrackedObject : MonoBehaviour
{
    public enum EIndex { None = -1, Hmd = 0 }
    private SteamVR_Behaviour_Pose pose;
    public EIndex index => pose != null ? (EIndex)pose.GetDeviceIndex() : EIndex.None;
    public bool isValid => pose != null && pose.isValid;
    private void Awake() => pose = GetComponent<SteamVR_Behaviour_Pose>();
}
