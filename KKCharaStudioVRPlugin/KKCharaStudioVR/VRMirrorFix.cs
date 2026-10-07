using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR;

// Studio mirrors use MirrorReflection (Yamada si_mirror: FX/MirrorReflection_Transparent).
// That shader samples _ReflectionTex with tex2Dproj and does not declare [PerRendererData],
// so a MaterialPropertyBlock never reaches the GPU. Studio also instances renderer.materials
// when a scene recolors an item; writing only sharedMaterials leaves the instance on the
// alpha mask that the prefab assigns to _ReflectionTex.
//
// The shader has one reflection texture and builds screen UVs from the eye that is
// drawing the glass. MultiPass draws the left eye, then the right eye. OnWillRenderObject
// is not a stereo camera callback, so stereoActiveEye stays Left or Mono for both
// visits and a single bind of the left texture makes the right eye sample the left
// reflection. That image is stuck to the headset and slides when the head moves.
// The previous bind of whichever texture was rendered last did the opposite: the
// right eye was stable and the left eye was not. Each visit binds only its own eye.
//
// The reflection camera must stay mono. Its view is that eye's view times the
// Householder reflection. Unity camera space looks down -Z, so a view built from
// TRS().inverse without the Z flip looks backwards and the oblique near plane clips
// the whole frustum (black / transparent mirror). The oblique matrix is built from
// the same eye's projection. The right eye uses SteamVR.instance.eyes[1] when
// Unity's stereo views and projections are not a real pair. If the clip plane
// faces the wrong way or is singular, fall back to that eye's projection.
internal static class VRMirrorFix
{
    private sealed class VRMirrorData
    {
        public Camera reflectionCamera;
        public RenderTexture leftTexture;
        public RenderTexture rightTexture;
        public MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        public int lastFrameRendered = -1;
        public int targetSize = 1024;
        public int headCallbackFrame = -1;
        public int headCallbacks;
        public int leftRenderedFrame = -1;
        public int rightRenderedFrame = -1;
        public VRMirrorStereoPolicy.ViewSource leftViewSource;
        public VRMirrorStereoPolicy.ViewSource rightViewSource;
    }

    private struct StereoProbe
    {
        public bool viewsDistinct;
        public bool projsDistinct;
        public bool viewMatchesLeft;
        public bool viewMatchesRight;
        public bool projMatchesLeft;
        public bool projMatchesRight;
        public Matrix4x4 leftView;
        public Matrix4x4 rightView;
        public Matrix4x4 leftProj;
        public Matrix4x4 rightProj;
        public bool hasLeft;
        public bool hasRight;
    }

    private struct EyeRender
    {
        public VRMirrorStereoPolicy.ViewSource view;
        public VRMirrorStereoPolicy.ProjSource proj;
    }

    private static readonly Dictionary<MirrorReflection, VRMirrorData> mirrorData = new Dictionary<MirrorReflection, VRMirrorData>();

    private static readonly int propReflectionTex = Shader.PropertyToID("_ReflectionTex");
    private static readonly int propLeftReflectionTex = Shader.PropertyToID("_LeftReflectionTex");
    private static readonly int propRightReflectionTex = Shader.PropertyToID("_RightReflectionTex");
    private static readonly int propReflectionTexture = Shader.PropertyToID("_ReflectionTexture");
    private static readonly int propMirrorTex = Shader.PropertyToID("_MirrorTex");
    private static readonly int propColor = Shader.PropertyToID("_Color");
    private static readonly int[] strengthProperties =
    {
        Shader.PropertyToID("_ReflectionStrength"),
        Shader.PropertyToID("_ReflectIntensity"),
        Shader.PropertyToID("_Intensity")
    };

    // Default + TransparentFX + the layers the studio task calls out
    // (8/9/10/11/20/21) plus Chara (10) and Map (11). Yamada ships m_Bits == 3072,
    // which is only Chara|Map, so props on Default never reach the stock camera.
    private const int SceneLayers =
        (1 << 0) | (1 << 1) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 20) | (1 << 21);

    private static bool installed;
    private static bool loggedFailure;
    private static bool loggedLeft;
    private static bool loggedRight;
    private static bool loggedCenter;
    private static bool loggedStillStereo;
    private static bool loggedTexture;
    private static bool loggedOblique;
    private static bool loggedManualView;
    private static bool loggedAdopt;
    private static bool loggedRightPass;
    private static int preRenderFrame = -1;
    private static int preRenderPass = -1;
    private static Camera forcingMono;
    private static int insideDepth;
    private static int insideFrame = -1;

    private static FieldInfo insideRendering;
    private static FieldInfo reflectionCameras;
    private static FieldInfo reflectionTexture;
    private static FieldInfo oldTextureSize;

    internal static void Install()
    {
        if (installed)
            return;

        BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        insideRendering = typeof(MirrorReflection).GetField("s_InsideRendering", BindingFlags.Static | BindingFlags.NonPublic);
        reflectionCameras = typeof(MirrorReflection).GetField("m_ReflectionCameras", instance);
        reflectionTexture = typeof(MirrorReflection).GetField("m_ReflectionTexture", instance);
        oldTextureSize = typeof(MirrorReflection).GetField("m_OldReflectionTextureSize", instance);

        MethodInfo onWill = typeof(MirrorReflection).GetMethod(
            "OnWillRenderObject",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo render = typeof(Camera).GetMethod("Render", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        MethodInfo onDisable = typeof(MirrorReflection).GetMethod(
            "OnDisable",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (insideRendering == null || onWill == null || render == null)
        {
            VRLog.Warn("VRMirrorFix could not find MirrorReflection.OnWillRenderObject or Camera.Render.");
            return;
        }

        var harmony = new Harmony("KKCharaStudioVR.MirrorFix");
        harmony.Patch(onWill, prefix: new HarmonyMethod(typeof(VRMirrorFix), nameof(OnWillRenderPrefix)));
        harmony.Patch(render, prefix: new HarmonyMethod(typeof(VRMirrorFix), nameof(CameraRenderPrefix)));
        if (onDisable != null)
            harmony.Patch(onDisable, prefix: new HarmonyMethod(typeof(VRMirrorFix), nameof(OnDisablePrefix)));

        Camera.onPreRender += OnCameraPreRender;

        var watcher = new GameObject("VRMirrorFix");
        watcher.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(watcher);
        watcher.AddComponent<VRMirrorWatcher>();

        installed = true;
        VRLog.Info("VRMirrorFix installed: per-eye reflection bind, SteamVR eyes[1] fallback, oblique-clip fallback.");
    }

    [HarmonyPriority(Priority.Last)]
    private static void CameraRenderPrefix(Camera __instance)
    {
        if (__instance == null)
            return;
        if (__instance != forcingMono && (__instance.name == null || !__instance.name.StartsWith("Mirror Refl Camera")))
            return;
        if (__instance.stereoTargetEye == StereoTargetEyeMask.None)
            return;
        __instance.stereoTargetEye = StereoTargetEyeMask.None;
        if (loggedStillStereo)
            return;
        loggedStillStereo = true;
        VRLog.Warn("Mirror reflection camera was still stereo at Render. Forced StereoTargetEyeMask.None so HMD tracking cannot override reflection pose.");
    }

    private static void OnDisablePrefix(MirrorReflection __instance)
    {
        if (__instance != null && mirrorData.TryGetValue(__instance, out VRMirrorData data))
        {
            CleanupMirrorData(data);
            mirrorData.Remove(__instance);
        }
    }

    private static bool OnWillRenderPrefix(MirrorReflection __instance)
    {
        if (__instance == null || !__instance.enabled)
            return false;

        // Desktop mode keeps the stock mirror.
        if (!VR.Active)
            return true;

        // s_InsideRendering is process-wide. A cover camera, a leaked native flag,
        // or our own reflection pass must not swallow the headset's callback for
        // the rest of the frame. Re-entry is tracked separately and cleared in finally.
        if (Inside())
        {
            if (insideDepth > 0 && insideFrame == Time.frameCount)
                return false;
            SetInside(false);
            insideDepth = 0;
        }

        Renderer renderer = __instance.GetComponent<Renderer>();
        if (!RendererUsable(renderer))
            return false;

        Camera current = Camera.current;
        if (IsReflectionCamera(current))
            return false;

        Camera vrHead = GetVRHeadCamera(current);
        if (vrHead == null)
            return false;

        // Mask-less cover / UI cameras do not draw the mirror. Returning false used
        // to skip the only update and suppress the stock mirror, leaving it blank.
        // Every camera that actually draws the mirror still refreshes the texture,
        // because tex2Dproj UVs belong to Camera.current, not to a different rig.
        if (IsIgnorableCamera(current, vrHead))
            return false;

        Camera source = current != null && current.cullingMask != 0 ? current : vrHead;
        VRMirrorData data = GetOrCreateVRMirrorData(__instance, vrHead);
        if (data == null || data.reflectionCamera == null || data.leftTexture == null || data.rightTexture == null)
            return false;

        bool isHead = source == vrHead;
        int pass = -1;
        if (isHead)
        {
            if (data.headCallbackFrame != Time.frameCount)
            {
                data.headCallbackFrame = Time.frameCount;
                data.headCallbacks = 0;
            }
            pass = VRMirrorStereoPolicy.PassForCallback(data.headCallbacks);
            data.headCallbacks++;
        }

        StereoProbe probe = ProbeStereo(source);
        VRMirrorStereoPolicy.EyeKind eye = VRMirrorStereoPolicy.Resolve(
            (int)source.stereoActiveEye,
            probe.viewMatchesLeft,
            probe.viewMatchesRight,
            probe.projMatchesLeft,
            probe.projMatchesRight,
            pass);
        RunMirrorPass(__instance, () => DrawResolvedEye(__instance, renderer, data, source, vrHead, probe, eye, pass, prefetchOtherEye: true));
        return false;
    }

    private static void OnCameraPreRender(Camera cam)
    {
        if (!VR.Active || cam == null || insideDepth > 0 || IsReflectionCamera(cam))
            return;
        Camera head = GetVRHeadCamera(cam);
        if (head == null || cam != head)
            return;

        if (preRenderFrame != Time.frameCount)
        {
            preRenderFrame = Time.frameCount;
            preRenderPass = 0;
        }
        else if (preRenderPass < 1)
            preRenderPass++;

        StereoProbe probe = ProbeStereo(cam);
        bool rightEye = cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right
            || (probe.viewMatchesRight && !probe.viewMatchesLeft)
            || (probe.projMatchesRight && !probe.projMatchesLeft)
            || (preRenderPass == 1 && cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Mono);
        if (!rightEye)
            return;

        // OnPreRender is a real stereo callback, so an explicit Right (or the
        // second pass when the API says Mono) can bind the right texture even
        // if OnWillRenderObject never runs for that eye. A later OnWillRenderObject
        // binds the same eye again before the glass is drawn.
        List<MirrorReflection> mirrors = null;
        foreach (KeyValuePair<MirrorReflection, VRMirrorData> kvp in mirrorData)
        {
            if (kvp.Key == null || kvp.Value == null || kvp.Value.reflectionCamera == null || kvp.Value.rightTexture == null)
                continue;
            mirrors ??= new List<MirrorReflection>();
            mirrors.Add(kvp.Key);
        }
        if (mirrors == null)
            return;
        for (int i = 0; i < mirrors.Count; i++)
        {
            MirrorReflection mirror = mirrors[i];
            if (!mirrorData.TryGetValue(mirror, out VRMirrorData data))
                continue;
            Renderer renderer = mirror.GetComponent<Renderer>();
            if (!RendererUsable(renderer))
                continue;
            RunMirrorPass(mirror, () => DrawResolvedEye(mirror, renderer, data, cam, head, probe, VRMirrorStereoPolicy.EyeKind.Right, preRenderPass, prefetchOtherEye: false));
        }
    }

    private static void DrawResolvedEye(
        MirrorReflection mirror,
        Renderer renderer,
        VRMirrorData data,
        Camera source,
        Camera head,
        StereoProbe probe,
        VRMirrorStereoPolicy.EyeKind eye,
        int pass,
        bool prefetchOtherEye)
    {
        if (eye == VRMirrorStereoPolicy.EyeKind.Right)
        {
            EyeRender rendered = RenderResolvedEye(mirror, data, data.rightTexture, source, head, probe, VRMirrorStereoPolicy.EyeKind.Right);
            UpdateMaterialProperties(renderer, data, data.rightTexture);
            LogEyeOnce(VRMirrorStereoPolicy.EyeKind.Right, rendered, pass, source.stereoActiveEye);
            if (pass == 1 && source.stereoActiveEye != Camera.MonoOrStereoscopicEye.Right && !loggedRightPass)
            {
                loggedRightPass = true;
                VRLog.Info("VRMirrorFix: second stereo pass bound the right reflection. stereoActiveEye was " + source.stereoActiveEye + ".");
            }
        }
        else if (eye == VRMirrorStereoPolicy.EyeKind.Left)
        {
            EyeRender rendered = RenderResolvedEye(mirror, data, data.leftTexture, source, head, probe, VRMirrorStereoPolicy.EyeKind.Left);
            if (prefetchOtherEye)
                RenderResolvedEye(mirror, data, data.rightTexture, source, head, probe, VRMirrorStereoPolicy.EyeKind.Right);
            UpdateMaterialProperties(renderer, data, data.leftTexture);
            LogEyeOnce(VRMirrorStereoPolicy.EyeKind.Left, rendered, pass, source.stereoActiveEye);
        }
        else
        {
            EyeRender rendered = RenderResolvedEye(mirror, data, data.leftTexture, source, head, probe, VRMirrorStereoPolicy.EyeKind.Mono);
            UpdateMaterialProperties(renderer, data, data.leftTexture);
            LogEyeOnce(VRMirrorStereoPolicy.EyeKind.Mono, rendered, pass, source.stereoActiveEye);
        }
        data.lastFrameRendered = Time.frameCount;
    }

    private static void RunMirrorPass(MirrorReflection mirror, Action draw)
    {
        int pixelLights = QualitySettings.pixelLightCount;
        insideDepth++;
        insideFrame = Time.frameCount;
        SetInside(true);
        HideKnownMirrors();
        try
        {
            if (mirror != null && mirror.m_DisablePixelLights)
                QualitySettings.pixelLightCount = 0;
            draw();
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }
        finally
        {
            RestoreHiddenMirrors();
            if (mirror != null && mirror.m_DisablePixelLights)
                QualitySettings.pixelLightCount = pixelLights;
            forcingMono = null;
            insideDepth--;
            if (insideDepth <= 0)
            {
                insideDepth = 0;
                SetInside(false);
            }
        }
    }

    private static Camera GetVRHeadCamera(Camera current)
    {
        if (VR.Camera != null && VR.Camera.SteamCam != null && VR.Camera.SteamCam.camera != null)
            return VR.Camera.SteamCam.camera;
        if (VRCamera.Instance != null && VRCamera.Instance.MainCamera != null)
            return VRCamera.Instance.MainCamera;
        if (current != null && current.cullingMask != 0 && !IsCoverName(current))
            return current;
        return null;
    }

    private static EyeRender RenderResolvedEye(
        MirrorReflection mirror,
        VRMirrorData data,
        RenderTexture targetTexture,
        Camera source,
        Camera head,
        StereoProbe probe,
        VRMirrorStereoPolicy.EyeKind eye)
    {
        EyeRender result = default;
        if (source == null || data == null || data.reflectionCamera == null || targetTexture == null)
            return result;

        GetEyeMatrices(source, head != null ? head : source, eye, probe, out Matrix4x4 eyeView, out Matrix4x4 eyeProj, out Vector3 eyePos, out Quaternion eyeRot, out result.view, out result.proj);
        bool right = eye == VRMirrorStereoPolicy.EyeKind.Right;
        int renderedFrame = right ? data.rightRenderedFrame : data.leftRenderedFrame;
        VRMirrorStereoPolicy.ViewSource previous = right ? data.rightViewSource : data.leftViewSource;
        if (!VRMirrorStereoPolicy.ShouldRender(renderedFrame, Time.frameCount, previous, result.view))
            return result;

        RenderMirrorEye(mirror, data.reflectionCamera, targetTexture, source, eyeView, eyeProj, eyePos, eyeRot);
        if (right)
        {
            data.rightRenderedFrame = Time.frameCount;
            data.rightViewSource = result.view;
        }
        else
        {
            data.leftRenderedFrame = Time.frameCount;
            data.leftViewSource = result.view;
        }
        return result;
    }

    private static void RenderMirrorEye(
        MirrorReflection mirror,
        Camera reflectionCamera,
        RenderTexture targetTexture,
        Camera source,
        Matrix4x4 eyeView,
        Matrix4x4 eyeProj,
        Vector3 eyePos,
        Quaternion eyeRot)
    {
        if (source == null || reflectionCamera == null || targetTexture == null)
            return;

        Vector3 planePoint = mirror.transform.position;
        float clipOffset = mirror.m_ClipPlaneOffset;
        if (clipOffset < 0.001f)
            clipOffset = 0.02f;

        Vector3 planeNormal = ResolvePlaneNormal(mirror, eyePos);
        Vector3 toEye = eyePos - planePoint;
        float facing = VRMirrorObliquePolicy.FacingDot(
            toEye.x, toEye.y, toEye.z, planeNormal.x, planeNormal.y, planeNormal.z);
        Matrix4x4 reflectedView = BuildReflectedView(planePoint, planeNormal, clipOffset, eyeView, out Matrix4x4 reflection);
        Matrix4x4 projection = eyeProj;
        Vector4 clipPlane = CameraSpacePlane(reflectedView, planePoint, planeNormal, clipOffset, 1f);
        // Behind the glass, parallel to it, or an exploded oblique matrix: keep the
        // eye projection. CalculateObliqueMatrix would otherwise clip the frustum empty.
        if (VRMirrorObliquePolicy.ShouldApplyOblique(facing)
            && TryOblique(eyeProj, clipPlane, out Matrix4x4 oblique)
            && VRMirrorObliquePolicy.IsStableProjection(MaxAbsComponent(oblique)))
        {
            projection = oblique;
        }
        else if (!loggedOblique)
        {
            loggedOblique = true;
            VRLog.Info("VRMirrorFix: oblique clip rejected (facing " + facing.ToString("0.###") + "); using the eye projection so the mirror is not empty.");
        }

        ReleaseFromHead(reflectionCamera);
        UpdateCameraModes(source, reflectionCamera);
        reflectionCamera.allowMSAA = false;
        reflectionCamera.allowHDR = false;
        reflectionCamera.usePhysicalProperties = false;
        reflectionCamera.cullingMask = BuildCullingMask(mirror, source);
        reflectionCamera.targetTexture = targetTexture;
        reflectionCamera.rect = new Rect(0f, 0f, 1f, 1f);

        // Transform writes in Unity 2019 reset worldToCameraMatrix. Pose first, then
        // write both matrices and render without touching the transform again.
        Vector3 reflectedPos = reflection.MultiplyPoint(eyePos);
        Quaternion reflectedRot = ReflectRotation(eyeRot, planeNormal);
        reflectionCamera.transform.SetPositionAndRotation(reflectedPos, reflectedRot);
        reflectionCamera.worldToCameraMatrix = reflectedView;
        reflectionCamera.projectionMatrix = projection;
        reflectionCamera.transform.hasChanged = false;

        bool prevInvert = GL.invertCulling;
        GL.invertCulling = !prevInvert;
        try
        {
            forcingMono = reflectionCamera;
            reflectionCamera.stereoTargetEye = StereoTargetEyeMask.None;
            reflectionCamera.Render();
        }
        finally
        {
            GL.invertCulling = prevInvert;
            forcingMono = null;
        }
    }

    private static StereoProbe ProbeStereo(Camera source)
    {
        StereoProbe probe = default;
        if (source == null)
            return probe;
        probe.hasLeft = TryReadStereo(source, Camera.StereoscopicEye.Left, out probe.leftView, out probe.leftProj);
        probe.hasRight = TryReadStereo(source, Camera.StereoscopicEye.Right, out probe.rightView, out probe.rightProj);
        probe.viewsDistinct = probe.hasLeft && probe.hasRight && !ApproximatelyLoose(probe.leftView, probe.rightView);
        probe.projsDistinct = probe.hasLeft && probe.hasRight && !ApproximatelyLoose(probe.leftProj, probe.rightProj);
        Matrix4x4 liveView = source.worldToCameraMatrix;
        Matrix4x4 liveProj = source.projectionMatrix;
        if (probe.viewsDistinct && Usable(liveView))
        {
            probe.viewMatchesLeft = ApproximatelyLoose(liveView, probe.leftView);
            probe.viewMatchesRight = ApproximatelyLoose(liveView, probe.rightView);
        }
        if (probe.projsDistinct && Usable(liveProj))
        {
            probe.projMatchesLeft = ApproximatelyLoose(liveProj, probe.leftProj);
            probe.projMatchesRight = ApproximatelyLoose(liveProj, probe.rightProj);
        }
        return probe;
    }

    private static bool TryReadStereo(Camera source, Camera.StereoscopicEye eye, out Matrix4x4 view, out Matrix4x4 projection)
    {
        view = default;
        projection = default;
        try
        {
            view = source.GetStereoViewMatrix(eye);
            projection = source.GetStereoProjectionMatrix(eye);
            return Usable(view) && Usable(projection);
        }
        catch
        {
            return false;
        }
    }

    private static void GetEyeMatrices(
        Camera source,
        Camera head,
        VRMirrorStereoPolicy.EyeKind eye,
        StereoProbe probe,
        out Matrix4x4 view,
        out Matrix4x4 projection,
        out Vector3 eyePos,
        out Quaternion eyeRot,
        out VRMirrorStereoPolicy.ViewSource viewSource,
        out VRMirrorStereoPolicy.ProjSource projSource)
    {
        bool mono = eye == VRMirrorStereoPolicy.EyeKind.Mono;
        bool right = eye == VRMirrorStereoPolicy.EyeKind.Right;
        bool liveView = !mono && (right ? probe.viewMatchesRight : probe.viewMatchesLeft);
        bool liveProj = !mono && (right ? probe.projMatchesRight : probe.projMatchesLeft);
        viewSource = VRMirrorStereoPolicy.SelectView(liveView, probe.viewsDistinct, probe.projsDistinct, mono);
        projSource = VRMirrorStereoPolicy.SelectProj(liveProj, probe.projsDistinct, mono);
        Camera.StereoscopicEye stereoEye = right ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;

        eyePos = source.transform.position;
        eyeRot = source.transform.rotation;
        view = ManualView(eyePos, eyeRot);
        projection = SymmetricProjection(source);

        if (viewSource == VRMirrorStereoPolicy.ViewSource.UnityStereo && TrySelectStereo(probe, right, out Matrix4x4 stereoView, out _))
        {
            view = stereoView;
            eyePos = CameraPositionFromView(stereoView, eyePos);
        }
        else if (viewSource == VRMirrorStereoPolicy.ViewSource.SteamVr
            && TrySteamPose(head, eye, out Vector3 steamPos, out Quaternion steamRot))
        {
            eyePos = steamPos;
            eyeRot = steamRot;
            view = ManualView(steamPos, steamRot);
            if (!loggedManualView)
            {
                loggedManualView = true;
                VRLog.Info("VRMirrorFix: Unity stereo eyes were identical. " + eye + " reflection uses SteamVR.instance.eyes[" + VRMirrorStereoPolicy.SteamEyeIndex(eye) + "] with a Z-flipped view.");
            }
        }
        else
        {
            Matrix4x4 currentView = source.worldToCameraMatrix;
            if (Usable(currentView))
                view = currentView;
            eyePos = CameraPositionFromView(view, eyePos);
            viewSource = VRMirrorStereoPolicy.ViewSource.Live;
        }

        bool foreign = right
            ? probe.projMatchesLeft && !probe.projMatchesRight
            : probe.projMatchesRight && !probe.projMatchesLeft;
        if (projSource == VRMirrorStereoPolicy.ProjSource.UnityStereo && TrySelectStereo(probe, right, out _, out Matrix4x4 stereoProj))
            projection = stereoProj;
        else if (projSource == VRMirrorStereoPolicy.ProjSource.SteamRaw && TrySteamRawProjection(source, stereoEye, out Matrix4x4 rawProj))
            projection = rawProj;
        else if (projSource == VRMirrorStereoPolicy.ProjSource.Live && !foreign && Usable(source.projectionMatrix))
            projection = source.projectionMatrix;
        else if (TrySelectStereo(probe, right, out _, out Matrix4x4 eyeProj) && (probe.projsDistinct || projSource != VRMirrorStereoPolicy.ProjSource.SteamRaw))
            projection = eyeProj;
    }

    private static bool TrySelectStereo(StereoProbe probe, bool right, out Matrix4x4 view, out Matrix4x4 projection)
    {
        if (right)
        {
            view = probe.rightView;
            projection = probe.rightProj;
            return probe.hasRight;
        }
        view = probe.leftView;
        projection = probe.leftProj;
        return probe.hasLeft;
    }

    private static bool TrySteamPose(Camera head, VRMirrorStereoPolicy.EyeKind eye, out Vector3 pos, out Quaternion rot)
    {
        pos = head != null ? head.transform.position : Vector3.zero;
        rot = head != null ? head.transform.rotation : Quaternion.identity;
        if (head == null || eye == VRMirrorStereoPolicy.EyeKind.Mono)
            return false;
        int eyeIndex = VRMirrorStereoPolicy.SteamEyeIndex(eye);
        try
        {
            SteamVR steam = SteamVR.instance;
            if (steam == null || steam.eyes == null || steam.eyes.Length <= eyeIndex)
                return false;
            SteamVR_Utils.RigidTransform local = steam.eyes[eyeIndex];
            pos = head.transform.TransformPoint(local.pos);
            rot = head.transform.rotation * local.rot;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySteamRawProjection(Camera source, Camera.StereoscopicEye eye, out Matrix4x4 projection)
    {
        projection = default;
        try
        {
            SteamVR steam = SteamVR.instance;
            if (steam == null || steam.hmd == null || source == null)
                return false;
            EVREye vrEye = eye == Camera.StereoscopicEye.Right ? EVREye.Eye_Right : EVREye.Eye_Left;
            float rawLeft = 0f;
            float rawRight = 0f;
            float rawTop = 0f;
            float rawBottom = 0f;
            steam.hmd.GetProjectionRaw(vrEye, ref rawLeft, ref rawRight, ref rawTop, ref rawBottom);
            float near = source.nearClipPlane > 0.001f ? source.nearClipPlane : 0.01f;
            float far = source.farClipPlane > near + 0.1f ? source.farClipPlane : near + 1000f;
            if (!VRMirrorStereoPolicy.TryRawFrustum(rawLeft, rawRight, rawTop, rawBottom, near, far, out float left, out float right, out float bottom, out float top))
                return false;
            projection = Matrix4x4.Frustum(left, right, bottom, top, near, far);
            return Usable(projection);
        }
        catch
        {
            return false;
        }
    }

    // Unity's worldToCameraMatrix is Scale(1,1,-1) * worldToLocal. TRS().inverse
    // alone looks down +Z and the oblique plane clips everything in front of the glass.
    private static Matrix4x4 ManualView(Vector3 position, Quaternion rotation)
    {
        Matrix4x4 worldToLocal = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
        return Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * worldToLocal;
    }

    private static Matrix4x4 SymmetricProjection(Camera source)
    {
        float near = source != null && source.nearClipPlane > 0.001f ? source.nearClipPlane : 0.01f;
        float far = source != null && source.farClipPlane > near + 0.1f ? source.farClipPlane : near + 1000f;
        float aspect = source != null && source.aspect > 0.01f ? source.aspect : 1f;
        float fov = source != null && source.fieldOfView > 1f ? source.fieldOfView : 60f;
        return Matrix4x4.Perspective(fov, aspect, near, far);
    }

    private static Vector3 CameraPositionFromView(Matrix4x4 view, Vector3 fallback)
    {
        if (!Usable(view))
            return fallback;
        Matrix4x4 inv = view.inverse;
        if (!Usable(inv))
            return fallback;
        Vector3 pos = inv.MultiplyPoint(Vector3.zero);
        if (float.IsNaN(pos.x) || float.IsInfinity(pos.x))
            return fallback;
        return pos;
    }

    private static Vector3 ResolvePlaneNormal(MirrorReflection mirror, Vector3 eyePos)
    {
        Transform transform = mirror.transform;
        Vector3 normal = transform.up;
        MeshFilter filter = mirror.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            // Yamada mo_koi_mirror00 is an XZ quad (extent Y == 0), then rotated
            // 90° on X so transform.up is the glass normal. Other props are flat
            // on a different local axis; follow the thinnest one.
            Vector3 ext = filter.sharedMesh.bounds.extents;
            float ax = Mathf.Abs(ext.x);
            float ay = Mathf.Abs(ext.y);
            float az = Mathf.Abs(ext.z);
            if (ay <= ax && ay <= az)
                normal = transform.up;
            else if (az <= ax && az <= ay)
                normal = transform.forward;
            else
                normal = transform.right;
        }
        if (normal.sqrMagnitude < 1e-8f)
            normal = Vector3.up;
        normal.Normalize();
        if (Vector3.Dot(eyePos - transform.position, normal) < 0f)
            normal = -normal;
        return normal;
    }

    private static Matrix4x4 BuildReflectedView(
        Vector3 planePoint,
        Vector3 planeNormal,
        float clipOffset,
        Matrix4x4 eyeView,
        out Matrix4x4 reflection)
    {
        Vector4 plane = new Vector4(
            planeNormal.x,
            planeNormal.y,
            planeNormal.z,
            -Vector3.Dot(planeNormal, planePoint) - clipOffset);
        reflection = CalculateReflectionMatrix(plane);
        return eyeView * reflection;
    }

    private static Quaternion ReflectRotation(Quaternion rotation, Vector3 normal)
    {
        Vector3 forward = Vector3.Reflect(rotation * Vector3.forward, normal);
        Vector3 up = Vector3.Reflect(rotation * Vector3.up, normal);
        if (forward.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f)
            return rotation;
        return Quaternion.LookRotation(forward, up);
    }

    private static bool TryOblique(Matrix4x4 projection, Vector4 clipPlane, out Matrix4x4 oblique)
    {
        oblique = projection;
        float mag = clipPlane.x * clipPlane.x + clipPlane.y * clipPlane.y + clipPlane.z * clipPlane.z;
        if (mag < 1e-8f || !Usable(projection))
            return false;
        // Camera origin must sit on the positive side of the clip plane.
        // w <= 0 means the normal faces away and the near plane deletes the scene.
        if (clipPlane.w <= 0.001f)
            return false;
        Matrix4x4 inv = projection.inverse;
        if (!Usable(inv))
            return false;
        float sx = clipPlane.x >= 0f ? 1f : -1f;
        float sy = clipPlane.y >= 0f ? 1f : -1f;
        Vector4 q = inv * new Vector4(sx, sy, 1f, 1f);
        float dot = clipPlane.x * q.x + clipPlane.y * q.y + clipPlane.z * q.z + clipPlane.w * q.w;
        if (float.IsNaN(dot) || float.IsInfinity(dot) || Mathf.Abs(dot) < 1e-4f)
            return false;

        Vector4 c = new Vector4(clipPlane.x, clipPlane.y, clipPlane.z, clipPlane.w) * (2f / dot);
        if (float.IsNaN(c.z) || float.IsInfinity(c.z) || Mathf.Abs(c.z) > 1000f || Mathf.Abs(c.w) > 1000f)
            return false;
        oblique = projection;
        oblique[2] = c.x - projection[3];
        oblique[6] = c.y - projection[7];
        oblique[10] = c.z - projection[11];
        oblique[14] = c.w - projection[15];
        return Usable(oblique);
    }

    private static int BuildCullingMask(MirrorReflection mirror, Camera source)
    {
        int mask = mirror.m_ReflectLayers.value;
        if (mask == 0)
            mask = SceneLayers;
        else
            mask |= SceneLayers;
        if (source != null && source.cullingMask != 0)
            mask |= source.cullingMask & 0x00FFFFFF;
        mask &= ~(1 << 4); // Water
        mask &= ~(1 << 5); // UI
        return mask;
    }

    private static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * plane.x * plane.x;
        m.m01 = -2f * plane.x * plane.y;
        m.m02 = -2f * plane.x * plane.z;
        m.m03 = -2f * plane.w * plane.x;

        m.m10 = -2f * plane.y * plane.x;
        m.m11 = 1f - 2f * plane.y * plane.y;
        m.m12 = -2f * plane.y * plane.z;
        m.m13 = -2f * plane.w * plane.y;

        m.m20 = -2f * plane.z * plane.x;
        m.m21 = -2f * plane.z * plane.y;
        m.m22 = 1f - 2f * plane.z * plane.z;
        m.m23 = -2f * plane.w * plane.z;

        m.m30 = 0f;
        m.m31 = 0f;
        m.m32 = 0f;
        m.m33 = 1f;
        return m;
    }

    private static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 pos, Vector3 normal, float clipOffset, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipOffset;
        Vector3 cPos = worldToCamera.MultiplyPoint(offsetPos);
        Vector3 cNormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cNormal.x, cNormal.y, cNormal.z, -Vector3.Dot(cPos, cNormal));
    }

    private static VRMirrorData GetOrCreateVRMirrorData(MirrorReflection mirror, Camera head)
    {
        PruneDeadMirrors();
        if (!mirrorData.TryGetValue(mirror, out VRMirrorData data))
        {
            data = new VRMirrorData();
            mirrorData[mirror] = data;
        }

        int targetSize = Mathf.Max(mirror.m_TextureSize, 1024);
        data.targetSize = targetSize;
        data.leftTexture = EnsureTexture(mirror, data.leftTexture, targetSize, "Left");
        data.rightTexture = EnsureTexture(mirror, data.rightTexture, targetSize, "Right");

        try
        {
            reflectionTexture?.SetValue(mirror, data.leftTexture);
            oldTextureSize?.SetValue(mirror, targetSize);
        }
        catch
        {
        }

        if (data.reflectionCamera == null)
            data.reflectionCamera = CreateReflectionCamera(mirror, head);
        else
            ReleaseFromHead(data.reflectionCamera);

        return data;
    }

    private static RenderTexture EnsureTexture(MirrorReflection mirror, RenderTexture current, int size, string eyeName)
    {
        if (current != null
            && current.width == size
            && current.height == size
            && current.depth == 24
            && current.format == RenderTextureFormat.ARGB32
            && current.vrUsage == VRTextureUsage.None
            && current.IsCreated())
        {
            return current;
        }
        if (current != null)
            UnityEngine.Object.DestroyImmediate(current);
        return CreateTexture(mirror, size, eyeName);
    }

    private static RenderTexture CreateTexture(MirrorReflection mirror, int size, string eyeName)
    {
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
        {
            name = $"__VRMirror_{eyeName}_{mirror.GetInstanceID()}",
            isPowerOfTwo = true,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            vrUsage = VRTextureUsage.None,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            hideFlags = HideFlags.DontSave
        };
        rt.Create();
        ForceMonoTexture(rt);
        return rt;
    }

    private static void ForceMonoTexture(RenderTexture texture)
    {
        if (texture == null || texture.vrUsage == VRTextureUsage.None)
            return;
        try
        {
            texture.Release();
            texture.vrUsage = VRTextureUsage.None;
            texture.Create();
        }
        catch (Exception ex)
        {
            if (loggedTexture)
                return;
            loggedTexture = true;
            VRLog.Warn("VRMirrorFix: Reflection texture could not be forced to mono: " + ex.Message);
        }
    }

    private static Camera CreateReflectionCamera(MirrorReflection mirror, Camera head)
    {
        GameObject go;
        if (mirror.cameraOriginal != null)
            go = UnityEngine.Object.Instantiate(mirror.cameraOriginal.gameObject);
        else
        {
            go = new GameObject("Mirror Refl Camera id" + mirror.GetInstanceID());
            go.AddComponent<Camera>();
            go.AddComponent<Skybox>();
            go.AddComponent<FlareLayer>();
        }
        go.name = "Mirror Refl Camera id" + mirror.GetInstanceID();
        go.hideFlags = HideFlags.HideAndDontSave;
        Camera cam = go.GetComponent<Camera>();
        cam.enabled = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        XRDevice.DisableAutoXRCameraTracking(cam, true);
        ReleaseFromHead(cam);
        return cam;
    }

    private static void ReleaseFromHead(Camera reflection)
    {
        if (reflection == null)
            return;
        reflection.enabled = false;
        reflection.stereoTargetEye = StereoTargetEyeMask.None;
        reflection.depth = -100f;
        reflection.rect = new Rect(0f, 0f, 1f, 1f);
        XRDevice.DisableAutoXRCameraTracking(reflection, true);

        GameObject go = reflection.gameObject;
        if (go.CompareTag("MainCamera"))
            go.tag = "Untagged";

        Component[] components = go.GetComponents<Component>();
        bool marked = go.GetComponent<VRMirrorReflectionMarker>() != null;
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null || IsKept(component))
                continue;
            if (IsHeadTracker(component))
            {
                UnityEngine.Object.DestroyImmediate(component);
                continue;
            }
            if (!marked && component is Behaviour behaviour)
                behaviour.enabled = false;
        }
        if (!marked)
            go.AddComponent<VRMirrorReflectionMarker>();

        Transform transform = reflection.transform;
        if (transform.parent != null)
            transform.SetParent(null, true);
        transform.localScale = Vector3.one;
    }

    private static void UpdateMaterialProperties(Renderer renderer, VRMirrorData data, RenderTexture activeTexture)
    {
        if (renderer == null || data == null || activeTexture == null)
            return;

        renderer.GetPropertyBlock(data.propertyBlock);
        data.propertyBlock.SetTexture(propReflectionTex, activeTexture);
        if (data.leftTexture != null)
            data.propertyBlock.SetTexture(propLeftReflectionTex, data.leftTexture);
        if (data.rightTexture != null)
            data.propertyBlock.SetTexture(propRightReflectionTex, data.rightTexture);
        renderer.SetPropertyBlock(data.propertyBlock);

        // Shared asset first so a later .materials clone copies the render texture.
        // Instances are what Studio actually draws after a recolor; the shader
        // ignores the property block because _ReflectionTex is not PerRendererData.
        BindMaterials(renderer.sharedMaterials, data, activeTexture);
        BindMaterials(renderer.materials, data, activeTexture);
        Material instance = renderer.material;
        if (instance != null)
            BindMaterial(instance, data, activeTexture);
    }

    private static void BindMaterials(Material[] materials, VRMirrorData data, RenderTexture activeTexture)
    {
        if (materials == null)
            return;
        for (int i = 0; i < materials.Length; i++)
            BindMaterial(materials[i], data, activeTexture);
    }

    private static void BindMaterial(Material material, VRMirrorData data, RenderTexture activeTexture)
    {
        if (material == null)
            return;
        SetTexture(material, propReflectionTex, activeTexture);
        SetTexture(material, propReflectionTexture, activeTexture);
        SetTexture(material, propMirrorTex, activeTexture);
        SetTexture(material, propLeftReflectionTex, data.leftTexture);
        SetTexture(material, propRightReflectionTex, data.rightTexture);
        if (material.HasProperty(propColor))
        {
            Color color = material.GetColor(propColor);
            if (color.a < 0.01f)
            {
                color.a = 1f;
                material.SetColor(propColor, color);
            }
        }
        for (int i = 0; i < strengthProperties.Length; i++)
        {
            int prop = strengthProperties[i];
            if (!material.HasProperty(prop))
                continue;
            if (material.GetFloat(prop) <= 0.001f)
                material.SetFloat(prop, 1f);
        }
    }

    private static void SetTexture(Material material, int property, Texture texture)
    {
        if (texture == null || !material.HasProperty(property))
            return;
        material.SetTexture(property, texture);
    }

    private static void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null || src == null)
            return;
        dest.clearFlags = src.clearFlags;
        Color background = src.backgroundColor;
        if (background.a < 0.99f)
            background.a = 1f;
        dest.backgroundColor = background;
        if (src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox skybox = src.GetComponent<Skybox>();
            Skybox skybox2 = dest.GetComponent<Skybox>();
            if (skybox == null || skybox.material == null)
            {
                if (skybox2 != null)
                    skybox2.enabled = false;
            }
            else
            {
                if (skybox2 == null)
                    skybox2 = dest.gameObject.AddComponent<Skybox>();
                skybox2.enabled = true;
                skybox2.material = skybox.material;
            }
        }
        else if (src.clearFlags == CameraClearFlags.Depth || src.clearFlags == CameraClearFlags.Nothing)
        {
            // An uninitialized color buffer is sampled as transparent by
            // FX/MirrorReflection_Transparent and the glass disappears.
            dest.clearFlags = CameraClearFlags.SolidColor;
            if (dest.backgroundColor.maxColorComponent < 0.001f && dest.backgroundColor.a < 0.99f)
                dest.backgroundColor = new Color(0f, 0f, 0f, 1f);
        }
        dest.farClipPlane = src.farClipPlane > src.nearClipPlane + 0.1f ? src.farClipPlane : src.nearClipPlane + 1000f;
        dest.nearClipPlane = src.nearClipPlane > 0.001f ? src.nearClipPlane : 0.01f;
        dest.orthographic = src.orthographic;
        dest.aspect = src.aspect > 0.01f ? src.aspect : 1f;
        dest.orthographicSize = src.orthographicSize;
        dest.renderingPath = src.renderingPath;
        dest.allowHDR = false;
        dest.allowMSAA = false;
        if (dest.stereoTargetEye == StereoTargetEyeMask.None && !dest.stereoEnabled)
            dest.fieldOfView = src.fieldOfView;
    }

    internal static void AdoptMirrorShaders()
    {
        Renderer[] renderers;
        try
        {
            renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
        }
        catch
        {
            return;
        }
        int adopted = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.GetComponent<MirrorReflection>() != null)
                continue;
            if (IsReflectionCamera(renderer.GetComponent<Camera>()))
                continue;
            Material material = renderer.sharedMaterial;
            if (material == null || material.shader == null)
                continue;
            string shaderName = material.shader.name ?? "";
            bool mirrorShader = shaderName.IndexOf("MirrorReflection", StringComparison.OrdinalIgnoreCase) >= 0
                || (shaderName.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 && material.HasProperty(propReflectionTex));
            if (!mirrorShader)
                continue;
            MirrorReflection mirror = renderer.gameObject.AddComponent<MirrorReflection>();
            mirror.m_DisablePixelLights = true;
            mirror.m_TextureSize = 1024;
            mirror.m_ClipPlaneOffset = 0.07f;
            LayerMask mask = default;
            mask.value = SceneLayers;
            mirror.m_ReflectLayers = mask;
            adopted++;
        }
        if (adopted > 0 && !loggedAdopt)
        {
            loggedAdopt = true;
            VRLog.Info("VRMirrorFix: attached MirrorReflection to " + adopted + " mirror-shader renderer(s) that did not have one.");
        }
    }

    private static void CleanupMirrorData(VRMirrorData data)
    {
        if (data == null)
            return;
        if (data.leftTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(data.leftTexture);
            data.leftTexture = null;
        }
        if (data.rightTexture != null)
        {
            UnityEngine.Object.DestroyImmediate(data.rightTexture);
            data.rightTexture = null;
        }
        if (data.reflectionCamera != null && data.reflectionCamera.gameObject != null)
        {
            UnityEngine.Object.DestroyImmediate(data.reflectionCamera.gameObject);
            data.reflectionCamera = null;
        }
    }

    private static void PruneDeadMirrors()
    {
        List<MirrorReflection> dead = null;
        foreach (var kvp in mirrorData)
        {
            if (kvp.Key == null)
            {
                dead ??= new List<MirrorReflection>();
                dead.Add(kvp.Key);
                CleanupMirrorData(kvp.Value);
            }
        }
        if (dead != null)
        {
            for (int i = 0; i < dead.Count; i++)
                mirrorData.Remove(dead[i]);
        }
    }

    private static readonly List<Renderer> hiddenMirrors = new List<Renderer>();
    private static readonly List<bool> hiddenPrevious = new List<bool>();

    private static void HideKnownMirrors()
    {
        hiddenMirrors.Clear();
        hiddenPrevious.Clear();
        foreach (KeyValuePair<MirrorReflection, VRMirrorData> kvp in mirrorData)
        {
            if (kvp.Key == null)
                continue;
            Renderer mirrorRenderer = kvp.Key.GetComponent<Renderer>();
            if (mirrorRenderer == null)
                continue;
            hiddenMirrors.Add(mirrorRenderer);
            hiddenPrevious.Add(mirrorRenderer.forceRenderingOff);
            mirrorRenderer.forceRenderingOff = true;
        }
    }

    private static void RestoreHiddenMirrors()
    {
        for (int i = 0; i < hiddenMirrors.Count; i++)
        {
            if (hiddenMirrors[i] != null)
                hiddenMirrors[i].forceRenderingOff = hiddenPrevious[i];
        }
        hiddenMirrors.Clear();
        hiddenPrevious.Clear();
    }

    private static bool RendererUsable(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled)
            return false;
        if (renderer.sharedMaterial != null)
            return true;
        Material[] materials = renderer.sharedMaterials;
        if (materials == null)
            return false;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
                return true;
        }
        return false;
    }

    private static bool IsIgnorableCamera(Camera current, Camera head)
    {
        if (current == null || current == head)
            return false;
        if (IsReflectionCamera(current))
            return true;
        if (current.cullingMask == 0 || current.cullingMask == (1 << 5))
            return true;
        return IsCoverName(current);
    }

    private static bool IsCoverName(Camera camera)
    {
        string name = camera != null ? camera.name : null;
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("Desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Companion", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("External Camera", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Preview Camera", StringComparison.Ordinal) >= 0;
    }

    private static bool IsReflectionCamera(Camera camera)
    {
        if (camera == null || camera.name == null)
            return false;
        return camera.name.StartsWith("Mirror Refl Camera", StringComparison.Ordinal);
    }

    private static bool IsKept(Component component)
    {
        return component is Transform
            || component is Camera
            || component is Skybox
            || component is FlareLayer
            || component is VRMirrorReflectionMarker;
    }

    private static bool IsHeadTracker(Component component)
    {
        string name = component.GetType().Name;
        return name == "SteamVR_Camera"
            || name == "TrackedPoseDriver"
            || name.IndexOf("PoseDriver", StringComparison.Ordinal) >= 0
            || name.StartsWith("SteamVR_", StringComparison.Ordinal);
    }

    private static float MaxAbsComponent(Matrix4x4 matrix)
    {
        float max = 0f;
        for (int i = 0; i < 16; i++)
        {
            float value = matrix[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
                return value;
            float abs = value >= 0f ? value : -value;
            if (abs > max)
                max = abs;
        }
        return max;
    }

    private static bool Usable(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; i++)
        {
            float value = matrix[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
        }
        return matrix.m00 != 0f || matrix.m11 != 0f || matrix.m22 != 0f;
    }

    private static bool ApproximatelyLoose(Matrix4x4 a, Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
        {
            if (Mathf.Abs(a[i] - b[i]) > 0.002f)
                return false;
        }
        return true;
    }

    private static void LogEyeOnce(VRMirrorStereoPolicy.EyeKind eye, EyeRender rendered, int pass, Camera.MonoOrStereoscopicEye reported)
    {
        if (eye == VRMirrorStereoPolicy.EyeKind.Left)
        {
            if (loggedLeft)
                return;
            loggedLeft = true;
        }
        else if (eye == VRMirrorStereoPolicy.EyeKind.Right)
        {
            if (loggedRight)
                return;
            loggedRight = true;
        }
        else
        {
            if (loggedCenter)
                return;
            loggedCenter = true;
        }
        VRLog.Info("VRMirrorFix: " + eye + " eye reflection bound (" + rendered.view + "/" + rendered.proj + ", pass " + pass + ", stereoActiveEye " + reported + ").");
    }

    private static void LogFailure(Exception ex)
    {
        if (loggedFailure)
            return;
        loggedFailure = true;
        VRLog.Warn("VRMirrorFix failed: " + ex);
    }

    private static bool Inside()
    {
        return insideRendering?.GetValue(null) is bool value && value;
    }

    private static void SetInside(bool value)
    {
        insideRendering?.SetValue(null, value);
    }
}

internal sealed class VRMirrorReflectionMarker : MonoBehaviour
{
}

internal sealed class VRMirrorWatcher : MonoBehaviour
{
    private float nextScan;

    private void Update()
    {
        if (!VR.Active || Time.unscaledTime < nextScan)
            return;
        nextScan = Time.unscaledTime + 3f;
        VRMirrorFix.AdoptMirrorShaders();
    }
}
