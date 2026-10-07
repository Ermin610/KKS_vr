using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VRGIN.Controls;
using VRGIN.Core;

namespace KKCharaStudioVR;

internal static class VRGameCompatibility
{
    private const float LoadingFlagWatchdogSeconds = 180f;
    private static float _loadingFlagSince = -1f;
    private static bool _loadingFlagWatchdogLogged;

    private static bool ApplyLoadingWatchdog(bool loading)
    {
        if (!loading)
        {
            _loadingFlagSince = -1f;
            _loadingFlagWatchdogLogged = false;
            return false;
        }

        if (_loadingFlagSince < 0f)
            _loadingFlagSince = Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - _loadingFlagSince < LoadingFlagWatchdogSeconds)
            return true;

        if (!_loadingFlagWatchdogLogged)
        {
            _loadingFlagWatchdogLogged = true;
            VRLog.Warn(
                "Manager.Scene loading flags stayed set for "
                + LoadingFlagWatchdogSeconds
                + "s; treating the load as finished so VR input is not held.");
        }
        return false;
    }

#if KKS
    internal const string PluginAssemblyName = "KKSCharaStudioVRPlugin";
    internal const string CameraSyncAssemblyName = "KKS_VR_CameraSync";
    internal const string CharacterApiTypeName = "KKAPI.Chara.CharacterApi, KKSAPI";
    internal const string AccessoryStatesAssemblyName = "KKS_Accessory_States";
    internal const string CoordinateAssemblyName = "KKS_CoordinateLoadOption";
    internal const string CoordinateNamespace = "CoordinateLoadOption.";
    internal const string CoordinatePluginTypeName = "CoordinateLoadOption.CoordinateLoadOption";
    internal const string BrowserFoldersAssemblyName = "KKS_BrowserFolders";
    internal static bool IsLoading
    {
        get
        {
            bool loading;
            try
            {
                loading = Manager.Scene.IsNowLoading || Manager.Scene.IsNowLoadingFade;
            }
            catch
            {
                loading = false;
            }
            return ApplyLoadingWatchdog(loading);
        }
    }
    internal static IEnumerable<ChaControl> Characters => Manager.Character.dictEntryChara.Values;
    internal static ChaListControl CharacterList => Manager.Character.chaListCtrl;
    internal static void DeleteCharacter(ChaControl character) => Manager.Character.DeleteChara(character, false);
    internal static void SaveAudioConfig()
    {
        // KKS persists this static config on shutdown; use the same XML writer
        // for immediate persistence after a wrist-menu change.
        var property = typeof(Manager.Config).GetProperty("xmlCtrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var control = property?.GetValue(null, null);
        if (control == null) throw new System.InvalidOperationException("KKS config XML writer is unavailable.");
        control.GetType().GetMethod("Write", System.Type.EmptyTypes).Invoke(control, null);
    }
    internal static void CaptureScreenshot(string path) => ScreenCapture.CaptureScreenshot(path);
    internal static int DeviceIndex(Controller controller) =>
        controller != null && controller.Tracking != null
            ? controller.Tracking.GetDeviceIndex()
            : -1;
#else
    internal const string PluginAssemblyName = "KKCharaStudioVRPlugin";
    internal const string CameraSyncAssemblyName = "KK_VR_CameraSync";
    internal const string CharacterApiTypeName = "KKAPI.Chara.CharacterApi, KKAPI";
    internal const string AccessoryStatesAssemblyName = "KK_Accessory_States";
    internal const string CoordinateAssemblyName = "KK_CoordinateLoadOption";
    internal const string CoordinateNamespace = "KK_CoordinateLoadOption.";
    internal const string CoordinatePluginTypeName = "KK_CoordinateLoadOption.KK_CoordinateLoadOption";
    internal const string BrowserFoldersAssemblyName = "KK_BrowserFolders";
    internal static bool IsLoading
    {
        get
        {
            bool loading = false;
            try
            {
                Manager.Scene scene = Singleton<Manager.Scene>.Instance;
                loading = scene != null && (scene.IsNowLoading || scene.IsNowLoadingFade);
            }
            catch
            {
                loading = false;
            }
            return ApplyLoadingWatchdog(loading);
        }
    }
    internal static IEnumerable<ChaControl> Characters => Singleton<Manager.Character>.Instance.dictEntryChara.Values;
    internal static ChaListControl CharacterList => Singleton<Manager.Character>.Instance?.chaListCtrl;
    internal static void DeleteCharacter(ChaControl character) => Singleton<Manager.Character>.Instance.DeleteChara(character, false);
    internal static void SaveAudioConfig() { var config = Singleton<Manager.Config>.Instance; if (config != null) config.Save(); }
    internal static void CaptureScreenshot(string path) => Application.CaptureScreenshot(path);
    internal static int DeviceIndex(Controller controller) =>
        controller != null && controller.Tracking != null
            ? (int)controller.Tracking.index
            : -1;
#endif

    internal static void InstallGameplayCameraGuards()
    {
        GameplayCameraGuard.Install();
    }

    // KKS_TrackingCamera clones Camera.main, writes its pose and viewport, and
    // can attach a CinemachineBrain that applies position, rotation, and FOV
    // every LateUpdate. That fights the headset and CameraSync, which owns the
    // VR origin from Studio.CameraControl. These plugins are not part of the
    // Studio process; the guards no-op when their assemblies are absent.
    static class GameplayCameraGuard
    {
        const float DanmenTextureInterval = 0.1f;
        static bool installed;
        static bool trackingLoaded;
        static bool loggedSafeCamera;
        static readonly Dictionary<int, float> danmenNextUpdate = new Dictionary<int, float>();

        internal static void Install()
        {
            if (installed)
                return;
            installed = true;

            var harmony = new Harmony("KKCharaStudioVR.GameplayCameraGuard");
            int patches = 0;
            var skip = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(SkipOriginal));
            var disable = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(DisableAndSkip));
            var brain = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(BrainLateUpdatePrefix));
            var safeCamera = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(NewCameraPrefix));
            var allowInput = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(AllowCameraInputPrefix));
            var undoLock = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(UndoMainCameraLock));
            var danmen = new HarmonyMethod(typeof(GameplayCameraGuard), nameof(DanmenTexturePrefix));

            foreach (Type camUtl in FindTypes("KKS_Common.CamUtl"))
            {
                patches += TryPatch(harmony, camUtl, "NewCamera", safeCamera, null);
                Type tool = Nested(camUtl, "MainCamTool");
                patches += TryPatch(harmony, tool, "Push", skip, null);
                patches += TryPatch(harmony, tool, "Reset", skip, null);
                patches += TryPatch(harmony, tool, "SetCameraPositionAndRotation", skip, null);
                Type settings = Nested(camUtl, "CameraSettings");
                patches += TryPatch(harmony, settings, "Restore", skip, null);
            }

            foreach (Type utl in FindTypes("KKS_Common.Utl"))
                patches += TryPatch(harmony, utl, "SetMainCameraPosition", skip, null);

            foreach (Type hooks in FindTypes("KKS_TrackingCamera.Hooks"))
            {
                trackingLoaded = true;
                patches += TryPatch(harmony, hooks, "BaseCameraControl_Ver2_InputKeyProc_Prefix", allowInput, null);
                patches += TryPatch(harmony, hooks, "KeyboardShortcut_Prefix", allowInput, null);
            }

            foreach (Type cameraBase in FindTypes("KKS_TrackingCamera.TCameraBase"))
            {
                trackingLoaded = true;
                patches += TryPatch(harmony, cameraBase, "Start", disable, null);
                Type state = Nested(cameraBase, "MainCameraState");
                patches += TryPatch(harmony, state, ".ctor", null, undoLock);
                patches += TryPatch(harmony, state, "Restore", skip, null);
            }

            string[] disableStart =
            {
                "KKS_TrackingCamera.TCamera",
                "KKS_TrackingCamera.TestCamera",
                "KKS_TrackingCamera.TestPanel",
                "KKS_TrackingCamera.CameraCoverMap",
                "KKS_TrackingCamera.CameraCoverPlayer",
                "KKS_TrackingCamera.CameraCoverPartner",
            };
            foreach (string name in disableStart)
            {
                foreach (Type type in FindTypes(name))
                {
                    trackingLoaded = true;
                    patches += TryPatch(harmony, type, "Start", disable, null);
                }
            }

            foreach (Type manager in FindTypes("KKS_TrackingCamera.TCameraManager"))
                patches += TryPatch(harmony, manager, "MoveCameraAdjustPosition", skip, null);
            foreach (Type main in FindTypes("KKS_TrackingCamera.MainController"))
                patches += TryPatch(harmony, main, "ShowMainCamera", skip, null);
            foreach (Type panel in FindTypes("KKS_TrackingCamera.CustomScenePanel"))
            {
                trackingLoaded = true;
                patches += TryPatch(harmony, Nested(panel, "FreeLookCamera"), "Initialize", disable, null);
            }

            Type cinemachineBrain = FirstType("Cinemachine.CinemachineBrain");
            if (cinemachineBrain == null)
            {
                try
                {
                    Assembly.Load("Cinemachine");
                }
                catch (Exception)
                {
                    cinemachineBrain = null;
                }
                cinemachineBrain = FirstType("Cinemachine.CinemachineBrain");
            }
            patches += TryPatch(harmony, cinemachineBrain, "LateUpdate", brain, null);

            foreach (Type chara in FindTypes("KKS_DanmenOverlay.CharaCtl"))
                patches += TryPatch(harmony, chara, "UpdateLvelTexture", danmen, null);

            ReleaseTrackingInputLock();
            int disabled = DisableActiveCameraDrivers();
            string freeH = FirstType("KKS_FreeHAutoPlay.FreeHAutoPlay") == null
                ? "KKS_FreeHAutoPlay is not loaded."
                : "KKS_FreeHAutoPlay.Update returns immediately outside HProc and does not write the camera; it stays enabled.";
            VRLog.Info(
                "Gameplay camera guard installed "
                + patches
                + " patches, disabled "
                + disabled
                + " live camera behaviours. "
                + (trackingLoaded
                    ? "KKS_TrackingCamera camera drivers will not clone or drive the VR view."
                    : "KKS_TrackingCamera is not loaded in this process.")
                + " "
                + freeH);
        }

        static bool SkipOriginal()
        {
            return false;
        }

        static bool DisableAndSkip(MonoBehaviour __instance)
        {
            if (__instance != null)
                __instance.enabled = false;
            return false;
        }

        static bool AllowCameraInputPrefix(ref bool __result)
        {
            __result = true;
            return false;
        }

        static bool BrainLateUpdatePrefix(MonoBehaviour __instance)
        {
            if (__instance == null)
                return true;
            Camera camera = __instance.GetComponent<Camera>();
            if (!IsProtectedCamera(camera))
                return true;
            __instance.enabled = false;
            return false;
        }

        static bool NewCameraPrefix(string name, Rect rect, Transform parent, bool light, ref Camera __result)
        {
            Camera source = Camera.main;
            var go = new GameObject(string.IsNullOrEmpty(name) ? "VRSafeCamera" : name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            Camera camera = go.AddComponent<Camera>();
            camera.tag = "Untagged";
            camera.rect = rect == Rect.zero ? new Rect(0f, 0f, 1f, 1f) : rect;
            if (source != null)
            {
                camera.clearFlags = source.clearFlags;
                camera.backgroundColor = source.backgroundColor;
                camera.nearClipPlane = Mathf.Max(source.nearClipPlane, 0.01f);
                camera.farClipPlane = source.farClipPlane;
                camera.fieldOfView = source.fieldOfView;
                camera.cullingMask = source.cullingMask;
                camera.depth = source.depth + 1f;
                camera.allowHDR = source.allowHDR;
            }
            camera.enabled = true;
            if (light && source != null)
                camera.allowHDR = source.allowHDR;
            __result = camera;
            if (!loggedSafeCamera)
            {
                loggedSafeCamera = true;
                VRLog.Info("Tracking camera creation no longer clones Camera.main.");
            }
            return false;
        }

        static void UndoMainCameraLock(object __instance)
        {
            if (__instance == null)
                return;
            Type hooks = __instance.GetType().Assembly.GetType("KKS_TrackingCamera.Hooks", false);
            if (hooks != null)
                Decrement(hooks, "DisableKeyboardShortcutCounter");
            FieldInfo field = __instance.GetType().GetField(
                "camCtrl",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var control = field == null ? null : field.GetValue(__instance) as Behaviour;
            if (control != null && !control.enabled)
            {
                control.enabled = true;
                if (hooks != null)
                    Decrement(hooks, "CameraControl_Ver2_LateUpdate_Prefix_Deter_Counter");
            }
        }

        static bool DanmenTexturePrefix(object __instance)
        {
            if (__instance == null)
                return true;
            int id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance);
            float now = Time.unscaledTime;
            float next;
            if (danmenNextUpdate.TryGetValue(id, out next) && now < next)
                return false;
            if (danmenNextUpdate.Count > 64)
                danmenNextUpdate.Clear();
            danmenNextUpdate[id] = now + DanmenTextureInterval;
            return true;
        }

        static bool IsProtectedCamera(Camera camera)
        {
            if (camera == null)
                return false;
            if (IsVrHeadsetCamera(camera))
                return true;
            return trackingLoaded && camera == Camera.main;
        }

        static bool IsVrHeadsetCamera(Camera camera)
        {
            try
            {
                if (VR.Camera != null)
                {
                    if (VR.Camera.SteamCam != null && VR.Camera.SteamCam.camera == camera)
                        return true;
                    Transform vrRoot = ((Component)VR.Camera).transform;
                    if (camera.transform == vrRoot || camera.transform.IsChildOf(vrRoot))
                        return true;
                    if (VR.Camera.SteamCam != null && VR.Camera.SteamCam.origin != null)
                    {
                        Transform origin = ((Component)VR.Camera.SteamCam.origin).transform;
                        if (camera.transform == origin || camera.transform.IsChildOf(origin))
                            return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return camera.GetComponent("SteamVR_Camera") != null;
        }

        static void ReleaseTrackingInputLock()
        {
            foreach (Type hooks in FindTypes("KKS_TrackingCamera.Hooks"))
            {
                int shortcuts = ReadInt(hooks, "DisableKeyboardShortcutCounter");
                int cameraLock = ReadInt(hooks, "CameraControl_Ver2_LateUpdate_Prefix_Deter_Counter");
                if (shortcuts <= 0 && cameraLock <= 0)
                    continue;
                SetInt(hooks, "DisableKeyboardShortcutCounter", 0);
                SetInt(hooks, "CameraControl_Ver2_LateUpdate_Prefix_Deter_Counter", 0);
                Camera main = Camera.main;
                if (main == null)
                    continue;
                foreach (Behaviour behaviour in main.GetComponents<Behaviour>())
                {
                    if (behaviour != null
                        && behaviour.GetType().Name == "CameraControl_Ver2"
                        && !behaviour.enabled)
                        behaviour.enabled = true;
                }
                VRLog.Info("Released KKS_TrackingCamera input lock held on the main camera.");
            }
        }

        static int DisableActiveCameraDrivers()
        {
            int disabled = 0;
            var names = new HashSet<string>
            {
                "KKS_TrackingCamera.TCamera",
                "KKS_TrackingCamera.TCameraBase",
                "KKS_TrackingCamera.TestCamera",
                "KKS_TrackingCamera.TestPanel",
                "KKS_TrackingCamera.CameraCoverMap",
                "KKS_TrackingCamera.CameraCoverPlayer",
                "KKS_TrackingCamera.CameraCoverPartner",
                "KKS_TrackingCamera.CustomScenePanel",
                "KKS_TrackingCamera.CustomScenePanel+FreeLookCamera",
            };
            MonoBehaviour[] behaviours;
            try
            {
                behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            }
            catch (Exception ex)
            {
                VRLog.Warn("Gameplay camera sweep failed: " + ex.Message);
                return 0;
            }
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;
                Type type = behaviour.GetType();
                string fullName = type.FullName;
                bool driver = fullName != null && names.Contains(fullName);
                if (!driver && fullName != null && fullName.StartsWith("KKS_TrackingCamera.CustomScenePanel+", StringComparison.Ordinal))
                    driver = type.Name == "FreeLookCamera";
                if (driver)
                {
                    if (behaviour.enabled)
                    {
                        behaviour.enabled = false;
                        disabled++;
                    }
                    continue;
                }
                if (type.Name == "CinemachineBrain" && behaviour.enabled)
                {
                    Camera camera = behaviour.GetComponent<Camera>();
                    if (IsProtectedCamera(camera))
                    {
                        behaviour.enabled = false;
                        disabled++;
                    }
                }
            }
            return disabled;
        }

        static int TryPatch(Harmony harmony, Type type, string method, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            if (type == null || (prefix == null && postfix == null))
                return 0;
            MethodBase target = null;
            try
            {
                if (method == ".ctor")
                    target = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                else
                    target = type.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch (Exception ex)
            {
                VRLog.Warn("Gameplay camera guard could not resolve " + type.FullName + "." + method + ": " + ex.Message);
                return 0;
            }
            if (target == null)
            {
                VRLog.Warn("Gameplay camera guard could not find " + type.FullName + "." + method + ".");
                return 0;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, postfix: postfix);
                return 1;
            }
            catch (Exception ex)
            {
                VRLog.Warn("Gameplay camera guard failed to patch " + type.FullName + "." + method + ": " + ex.Message);
                return 0;
            }
        }

        static Type Nested(Type parent, string name)
        {
            if (parent == null)
                return null;
            return parent.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
        }

        static Type FirstType(string fullName)
        {
            List<Type> types = FindTypes(fullName);
            return types.Count == 0 ? null : types[0];
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
            foreach (Assembly assembly in assemblies)
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(fullName, false);
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

        static int ReadInt(Type type, string fieldName)
        {
            FieldInfo field = StaticField(type, fieldName);
            if (field == null)
                return 0;
            object value = field.GetValue(null);
            return value is int ? (int)value : 0;
        }

        static void SetInt(Type type, string fieldName, int value)
        {
            FieldInfo field = StaticField(type, fieldName);
            if (field != null)
                field.SetValue(null, value);
        }

        static void Decrement(Type type, string fieldName)
        {
            int value = ReadInt(type, fieldName);
            if (value > 0)
                SetInt(type, fieldName, value - 1);
        }

        static FieldInfo StaticField(Type type, string fieldName)
        {
            return type == null
                ? null
                : type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
