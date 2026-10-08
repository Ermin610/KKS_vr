using UnityEngine;

namespace KKCharaStudioVR;

/// <summary>
/// Re-applies the saved background preset when Studio's main camera or the VR
/// camera appears or is replaced (startup, scene load, VR recovery). Without
/// this the wrist-menu background colour lasted only until the next restart.
/// </summary>
internal sealed class VRBackgroundPresetKeeper : MonoBehaviour
{
    private const float PollInterval = 0.5f;

    private static int _requestGeneration;

    private float _nextPoll;
    private int _appliedSignature;
    private int _appliedGeneration;

    /// <summary>Ask for one re-apply on the next poll (for example after a scene load).</summary>
    internal static void RequestReapply()
    {
        unchecked
        {
            _requestGeneration++;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextPoll)
            return;
        _nextPoll = Time.unscaledTime + PollInterval;

        int savedIndex = VRStudioSettingsService.GetSavedBackgroundPresetIndex();
        int studioCameraId;
        int vrCameraId;
        bool studioCameraReady = VRStudioSettingsService.TryGetBackgroundCameraIds(out studioCameraId, out vrCameraId);
        int signature = VRBackgroundPresetPolicy.CameraSignature(studioCameraId, vrCameraId);
        int generation = _requestGeneration;
        if (!VRBackgroundPresetPolicy.ShouldReapply(
                savedIndex,
                VRStudioSettingsService.BackgroundPresetCount,
                studioCameraReady,
                signature,
                _appliedSignature,
                generation,
                _appliedGeneration))
        {
            return;
        }

        string status;
        if (VRStudioSettingsService.SetBackgroundPreset(savedIndex, out status))
        {
            _appliedSignature = signature;
            _appliedGeneration = generation;
            VRGIN.Core.VRLog.Info("Saved background preset re-applied: " + status);
        }
    }
}
