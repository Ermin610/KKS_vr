using System;
using System.Globalization;

namespace KKCharaStudioVR;

/// <summary>
/// Figure scale range, the classic figure presets, and the slider mapping. The
/// slider is logarithmic so the 1/12..1/4 figure band gets as much travel as
/// 1/4..2x instead of being squeezed into the first tenth of the track.
/// </summary>
internal static class VRFigureScalePolicy
{
    internal const float MinScale = 0.05f;
    internal const float MaxScale = 2f;
    internal const float FineStepRatio = 1.02f;
    internal const float CoarseStepRatio = 1.1f;
    internal const float PresetTolerance = 0.02f;
    internal const float SettleTolerance = 0.002f;
    internal const float SmoothingRate = 14f;

    internal static readonly int[] PresetDenominators = { 1, 4, 6, 7, 12 };

    internal static float Clamp(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale))
            return 1f;
        if (scale < MinScale)
            return MinScale;
        if (scale > MaxScale)
            return MaxScale;
        return scale;
    }

    internal static float PresetScale(int denominator)
    {
        return denominator <= 0 ? 1f : Clamp(1f / denominator);
    }

    internal static float ToSlider(float scale)
    {
        double value = Math.Log(Clamp(scale) / MinScale) / Math.Log(MaxScale / MinScale);
        return Clamp01((float)value);
    }

    internal static float FromSlider(float fraction)
    {
        double value = MinScale * Math.Pow(MaxScale / MinScale, Clamp01(fraction));
        return Clamp((float)value);
    }

    internal static float SliderFraction(float localX, float trackXMin, float trackWidth)
    {
        if (!(trackWidth > 0f) || float.IsNaN(localX) || float.IsInfinity(localX))
            return 0f;
        return Clamp01((localX - trackXMin) / trackWidth);
    }

    internal static float Step(float scale, int steps, bool coarse)
    {
        double ratio = coarse ? CoarseStepRatio : FineStepRatio;
        return Clamp((float)(Clamp(scale) * Math.Pow(ratio, steps)));
    }

    /// <summary>
    /// Denominator of the preset the scale sits on, or 0 when it is between presets.
    /// </summary>
    internal static int MatchPreset(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale))
            return 0;
        foreach (int denominator in PresetDenominators)
        {
            float preset = PresetScale(denominator);
            if (Math.Abs(scale - preset) <= preset * PresetTolerance)
                return denominator;
        }
        return 0;
    }

    /// <summary>
    /// Frame-rate independent approach that lands exactly on the target once it
    /// is close, so a settled scale stops being rewritten.
    /// </summary>
    internal static float Approach(float current, float target, float deltaTime)
    {
        target = Clamp(target);
        if (float.IsNaN(current) || float.IsInfinity(current))
            return target;
        if (!(deltaTime > 0f))
            return current;
        float alpha = Clamp01(1f - (float)Math.Exp(-SmoothingRate * deltaTime));
        float next = current + (target - current) * alpha;
        return IsSettled(next, target) ? target : next;
    }

    internal static bool IsSettled(float current, float target)
    {
        return Math.Abs(current - target) <= Math.Abs(target) * SettleTolerance;
    }

    /// <summary>"1/7" style ratio for scales below life size, "1.25x" above.</summary>
    internal static string FormatRatio(float scale)
    {
        float value = Clamp(scale);
        if (value >= 0.995f)
            return value.ToString("0.00", CultureInfo.InvariantCulture) + "x";
        return "1/" + (1f / value).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || value < 0f)
            return 0f;
        return value > 1f ? 1f : value;
    }
}
