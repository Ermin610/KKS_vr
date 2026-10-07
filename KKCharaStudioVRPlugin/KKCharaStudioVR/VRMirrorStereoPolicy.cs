using System;

namespace KKCharaStudioVR;

// FX/MirrorReflection_Transparent samples one _ReflectionTex with the drawing
// eye's screen position. MultiPass draws the left eye, then the right eye.
// OnWillRenderObject often reports stereoActiveEye as Left (Unity's default
// while stereo is enabled) or Mono for both visits, so a bind that trusts
// that value leaves the left reflection on the material while the right eye
// samples it. The right eye then slides with the headset.
//
// The second visit to a mirror in a frame is the right eye. Its reflection
// has to be rendered from that eye's own view and projection: Unity's stereo
// pair when the two eyes differ, otherwise SteamVR.instance.eyes[1] and that
// eye's frustum. The oblique clip is built from the same projection.
internal static class VRMirrorStereoPolicy
{
    // UnityEngine.Camera.MonoOrStereoscopicEye
    public const int ActiveLeft = 0;
    public const int ActiveRight = 1;
    public const int ActiveMono = 2;

    public enum EyeKind
    {
        Left = 0,
        Right = 1,
        Mono = 2
    }

    public enum ViewSource
    {
        Live = 0,
        UnityStereo = 1,
        SteamVr = 2
    }

    public enum ProjSource
    {
        Live = 0,
        UnityStereo = 1,
        SteamRaw = 2
    }

    public static int PassForCallback(int callbackIndex)
    {
        if (callbackIndex < 0)
            return -1;
        return callbackIndex > 1 ? 1 : callbackIndex;
    }

    public static EyeKind Resolve(
        int stereoActiveEye,
        bool viewMatchesLeft,
        bool viewMatchesRight,
        bool projectionMatchesLeft,
        bool projectionMatchesRight,
        int hmdPass)
    {
        if (stereoActiveEye == ActiveRight)
            return EyeKind.Right;
        if (viewMatchesRight && !viewMatchesLeft)
            return EyeKind.Right;
        if (projectionMatchesRight && !projectionMatchesLeft)
            return EyeKind.Right;

        // Left is also the value Unity returns outside a stereo render callback.
        // The second time this mirror is drawn in the frame is the right eye,
        // even when the live matrix is still the left eye's matrix.
        if (hmdPass == 1)
            return EyeKind.Right;

        if (viewMatchesLeft && !viewMatchesRight)
            return EyeKind.Left;
        if (projectionMatchesLeft && !projectionMatchesRight)
            return EyeKind.Left;
        if (hmdPass == 0)
            return EyeKind.Left;
        if (stereoActiveEye == ActiveLeft)
            return EyeKind.Left;
        return EyeKind.Mono;
    }

    public static int TextureSlot(EyeKind eye)
    {
        return eye == EyeKind.Right ? 1 : 0;
    }

    public static int SteamEyeIndex(EyeKind eye)
    {
        return eye == EyeKind.Right ? 1 : 0;
    }

    public static ViewSource SelectView(bool liveMatchesThisEye, bool viewsDistinct, bool projectionsDistinct, bool mono)
    {
        if (mono || liveMatchesThisEye)
            return ViewSource.Live;
        if (viewsDistinct || projectionsDistinct)
            return ViewSource.UnityStereo;
        // Unity reported one view and one projection for both eyes. The right
        // eye must not reuse that camera; SteamVR.instance.eyes[1] carries the IPD.
        return ViewSource.SteamVr;
    }

    public static ProjSource SelectProj(bool liveProjectionMatchesThisEye, bool projectionsDistinct, bool mono)
    {
        if (mono || liveProjectionMatchesThisEye)
            return ProjSource.Live;
        if (projectionsDistinct)
            return ProjSource.UnityStereo;
        return ProjSource.SteamRaw;
    }

    public static bool ShouldRender(int renderedFrame, int frame, ViewSource previous, ViewSource next)
    {
        if (renderedFrame != frame)
            return true;
        if (previous == next)
            return false;
        // A live view was captured while that eye was actually bound. Keep it.
        return previous != ViewSource.Live;
    }

    // OpenVR GetProjectionRaw returns frustum-edge tangents (left, right, top, bottom).
    public static bool TryRawFrustum(
        float rawLeft,
        float rawRight,
        float rawTop,
        float rawBottom,
        float near,
        float far,
        out float left,
        out float right,
        out float bottom,
        out float top)
    {
        left = right = bottom = top = 0f;
        if (!Finite(rawLeft) || !Finite(rawRight) || !Finite(rawTop) || !Finite(rawBottom))
            return false;
        if (!(near > 0.001f) || !(far > near + 0.01f))
            return false;
        left = rawLeft * near;
        right = rawRight * near;
        bottom = rawBottom * near;
        top = rawTop * near;
        if (bottom > top)
        {
            float swap = bottom;
            bottom = top;
            top = swap;
        }
        return left < right && bottom < top;
    }

    private static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
