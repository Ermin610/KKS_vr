using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR;

// KKS_PostProcessingEffect attaches its stack with Camera.main. After VRGIN
// tags the headset camera MainCamera, that stack lands on the eye camera.
// Bloom, ambient occlusion, Sobel outlines, and Senga lines are the KKS grade
// and run on that camera. This hook does not skip or disable them. It only
// stops KKPE from rebuilding pose gizmos on both eyes.
internal static class VRVisualPluginHooks
{
    static bool installed;
    static readonly HashSet<string> Logged = new HashSet<string>();

    internal static void Install()
    {
        if (installed)
            return;
        installed = true;

        var harmony = new Harmony("KKCharaStudioVR.VisualPlugins");
        var oneEye = new HarmonyMethod(typeof(VRVisualPluginHooks), nameof(SkipSecondEye));
        int patches = 0;

        foreach (Type dispatcher in FindTypes("HSPE.CameraEventsDispatcher"))
            patches += TryPatch(harmony, dispatcher, "OnPreRender", oneEye, null);

        VRLog.Info(
            "Visual plugin guard installed "
            + patches
            + " patches. PostProcessLayer, SSAOPro, SobelOutline, and SengaEffect stay on the VR eye camera. KKPE gizmo pre-render on that camera runs for one eye only.");
    }

    static bool SkipSecondEye(Component __instance)
    {
        if (!IsEyeCamera(__instance))
            return true;
        Camera camera = __instance.GetComponent<Camera>();
        if (camera == null || camera.stereoActiveEye != Camera.MonoOrStereoscopicEye.Right)
            return true;
        if (Logged.Add("CameraEventsDispatcher"))
        {
            VRLog.Info(
                "KKPE CameraEventsDispatcher is on the VR eye camera. The right eye skips the gizmo rebuild so MultiPass does not run it twice.");
        }
        return false;
    }

    static bool IsEyeCamera(Component component)
    {
        if (component == null)
            return false;
        Camera camera = component as Camera;
        if (camera == null)
            camera = component.GetComponent<Camera>();
        Camera eye = EyeCamera();
        return camera != null && eye != null && camera == eye;
    }

    static Camera EyeCamera()
    {
        if (!VR.Active || VR.Camera == null || VR.Camera.SteamCam == null)
            return null;
        return VR.Camera.SteamCam.camera;
    }

    static int TryPatch(Harmony harmony, Type type, string method, HarmonyMethod prefix, HarmonyMethod postfix)
    {
        if (type == null || (prefix == null && postfix == null))
            return 0;
        MethodInfo target;
        try
        {
            target = type.GetMethod(
                method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Visual plugin guard could not resolve " + type.FullName + "." + method + ": " + ex.Message);
            return 0;
        }
        if (target == null)
        {
            VRLog.Warn("Visual plugin guard could not find " + type.FullName + "." + method + ".");
            return 0;
        }
        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            return 1;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Visual plugin guard failed to patch " + type.FullName + "." + method + ": " + ex.Message);
            return 0;
        }
    }

    static List<Type> FindTypes(string fullName)
    {
        var found = new List<Type>();
        Assembly[] assemblies;
        try
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies();
        }
        catch (Exception)
        {
            return found;
        }
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type;
            try
            {
                type = assemblies[i].GetType(fullName, false);
            }
            catch (Exception)
            {
                continue;
            }
            if (type != null)
                found.Add(type);
        }
        return found;
    }
}
