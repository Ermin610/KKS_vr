using System;

namespace KKCharaStudioVR;

// Decides when MirrorReflection's oblique near clip is safe to apply.
// A camera behind the mirror, or a view nearly parallel to the glass, makes
// CalculateObliqueMatrix slice the whole frustum and the reflection goes black.
internal static class VRMirrorObliquePolicy
{
    public const float MinFacingDot = 0.08f;
    public const float MaxStableComponent = 100000f;

    public static float FacingDot(float dx, float dy, float dz, float nx, float ny, float nz)
    {
        float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        float normalLength = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (length < 1e-5f || normalLength < 1e-5f)
            return float.NaN;
        return (dx * nx + dy * ny + dz * nz) / (length * normalLength);
    }

    public static bool ShouldApplyOblique(float facingDot)
    {
        if (float.IsNaN(facingDot) || float.IsInfinity(facingDot))
            return false;
        return facingDot >= MinFacingDot;
    }

    public static bool IsStableProjection(float maxAbsComponent)
    {
        if (float.IsNaN(maxAbsComponent) || float.IsInfinity(maxAbsComponent))
            return false;
        return maxAbsComponent > 0f && maxAbsComponent <= MaxStableComponent;
    }
}
