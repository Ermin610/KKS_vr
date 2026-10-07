using System;
using System.Collections.Generic;
using System.Text;
using Unity.XR.OpenVR;
using UnityEngine;
using UnityStandardAssets.ImageEffects;
using VRGIN.Core;

namespace KKCharaStudioVR;

// The eye camera keeps the KKS grade: HDR, bloom, ambient occlusion, and the
// post-process stack. Only full-screen effects that smear or refocus each eye
// are turned off. Depth and sun shafts are left at the values the scene set.
internal sealed class VRCameraPerformance : MonoBehaviour
{
    private static readonly List<Behaviour> Behaviours = new List<Behaviour>(32);
    private static readonly List<string> DisabledNames = new List<string>(8);
    private static readonly List<string> KeptNames = new List<string>(8);
    private static string _lastReport;
    private static bool _stereoLogged;

    private void Start()
    {
        LogStereoMode();
        ApplyNow();
    }

    private void LateUpdate()
    {
        // Scene load and the post-processing plugin can attach a layer a few
        // frames after the rig exists. A full scan every frame is unnecessary.
        if ((Time.frameCount & 15) != 0)
            return;
        ApplyNow();
    }

    internal static bool BlocksCopiedEffect(string typeName)
    {
        return IsComfortOnlyEffect(typeName);
    }

    internal static void ApplyNow()
    {
        Camera camera = EyeCamera();
        if (camera == null)
            return;

        DisabledNames.Clear();
        KeptNames.Clear();
        camera.GetComponents(Behaviours);
        for (int i = 0; i < Behaviours.Count; i++)
        {
            Behaviour behaviour = Behaviours[i];
            if (behaviour == null || behaviour == camera || !behaviour.enabled)
                continue;
            string typeName = behaviour.GetType().Name;
            if (IsCoreVisual(typeName))
            {
                KeptNames.Add(typeName);
                continue;
            }
            if (!IsComfortOnlyEffect(typeName))
                continue;

            behaviour.enabled = false;
            DisabledNames.Add(typeName);
        }

        // Bloom and the post stack sample a floating-point target. Turning this
        // off flattens the grade even when those components stay enabled.
        string hdr = "on";
        if (!camera.allowHDR)
        {
            camera.allowHDR = true;
            hdr = "restored";
        }

        bool stereoFixed = false;
        if (camera.stereoTargetEye != StereoTargetEyeMask.Both)
        {
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            stereoFixed = true;
        }

        string sunShafts = DescribeSunShafts(camera);
        string report = BuildReport(hdr, stereoFixed, sunShafts, camera);
        if (!string.Equals(report, _lastReport, StringComparison.Ordinal))
        {
            _lastReport = report;
            VRLog.Info("VR eye camera adjusted: " + report);
        }
    }

    private static void LogStereoMode()
    {
        if (_stereoLogged)
            return;
        _stereoLogged = true;
        try
        {
            OpenVRSettings settings = OpenVRSettings.GetSettings(false);
            string mode = settings == null ? "unknown" : settings.StereoRenderingMode.ToString();
            VRLog.Info(
                "OpenXR stereo mode is " + mode +
                ". MultiPass is required: KKS character shaders and ColorZOrder have no single-pass instanced variants. " +
                "The desktop cover camera stays StereoTargetEyeMask.None.");
        }
        catch (Exception exception)
        {
            VRLog.Warn("OpenXR stereo mode could not be read: " + exception.Message);
        }
    }

    private static Camera EyeCamera()
    {
        if (!VR.Active || VR.Camera == null || VR.Camera.SteamCam == null)
            return null;
        return VR.Camera.SteamCam.camera;
    }

    // Logged and left enabled. These are the anime grade.
    private static bool IsCoreVisual(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        if (typeName.IndexOf("AmplifyColor", StringComparison.Ordinal) >= 0)
            return true;
        return typeName.IndexOf("Bloom", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("AmbientOcclusion", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("AmplifyOcclusion", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("SSAO", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("Sobel", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("Senga", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("SunShaft", StringComparison.Ordinal) >= 0
            || typeName == "PostProcessLayer";
    }

    // Depth of field, motion blur, chromatic aberration, and screen-space GI
    // smear or refocus once per eye. They are not the outline / bloom grade.
    private static bool IsComfortOnlyEffect(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        if (IsCoreVisual(typeName))
            return false;
        return typeName.IndexOf("DepthOfField", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("MotionBlur", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("ChromaticAberration", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("SSGI", StringComparison.Ordinal) >= 0
            || typeName.IndexOf("ScreenSpaceGI", StringComparison.Ordinal) >= 0;
    }

    private static string DescribeSunShafts(Camera camera)
    {
        SunShafts shafts = camera.GetComponent<SunShafts>();
        if (shafts == null || !shafts.enabled)
            return "off";
        return "on/" + shafts.resolution;
    }

    private static string BuildReport(string hdr, bool stereoFixed, string sunShafts, Camera camera)
    {
        StringBuilder builder = new StringBuilder(160);
        builder.Append("disabled=");
        AppendNames(builder, DisabledNames);
        builder.Append("; kept=");
        AppendNames(builder, KeptNames);
        builder.Append("; depth=");
        builder.Append(camera.depthTextureMode.ToString());
        builder.Append("; hdr=");
        builder.Append(hdr);
        builder.Append("; stereo=");
        builder.Append(stereoFixed ? "forced-both" : camera.stereoTargetEye.ToString());
        builder.Append("; sunShafts=");
        builder.Append(sunShafts);
        return builder.ToString();
    }

    private static void AppendNames(StringBuilder builder, List<string> names)
    {
        if (names.Count == 0)
        {
            builder.Append("none");
            return;
        }
        for (int i = 0; i < names.Count; i++)
        {
            if (i != 0)
                builder.Append(',');
            builder.Append(names[i]);
        }
    }
}
