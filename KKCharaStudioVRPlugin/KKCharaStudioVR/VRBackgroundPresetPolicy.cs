namespace KKCharaStudioVR;

/// <summary>
/// Pure rules for the saved VR background colour (wrist menu presets such as
/// the green screen). The preset index is stored in the VR settings XML and
/// re-applied whenever Studio or VRGIN hands us a different camera, because
/// the colour lives on the camera objects and is lost with them.
/// </summary>
internal static class VRBackgroundPresetPolicy
{
    /// <summary>No saved preset: leave Studio's own background alone.</summary>
    internal const int Unset = -1;

    internal static bool HasSavedPreset(int savedIndex, int presetCount)
    {
        return presetCount > 0 && savedIndex >= 0 && savedIndex < presetCount;
    }

    /// <summary>Out-of-range values (hand-edited XML, fewer presets) become Unset.</summary>
    internal static int NormalizeSaved(int savedIndex, int presetCount)
    {
        return HasSavedPreset(savedIndex, presetCount) ? savedIndex : Unset;
    }

    /// <summary>Identifies the current pair of Studio and VR cameras (0 = missing).</summary>
    internal static int CameraSignature(int studioCameraId, int vrCameraId)
    {
        unchecked
        {
            return (studioCameraId * 397) ^ vrCameraId;
        }
    }

    /// <summary>
    /// Re-apply once per new camera pair or explicit request, never every frame,
    /// so the saved colour does not fight anything else that changes it later.
    /// </summary>
    internal static bool ShouldReapply(
        int savedIndex,
        int presetCount,
        bool studioCameraReady,
        int cameraSignature,
        int appliedSignature,
        int requestGeneration,
        int appliedGeneration)
    {
        if (!HasSavedPreset(savedIndex, presetCount) || !studioCameraReady)
            return false;
        return cameraSignature != appliedSignature || requestGeneration != appliedGeneration;
    }
}
