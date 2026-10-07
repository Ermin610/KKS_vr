using System;
using System.Collections;
using Unity.XR.OpenVR;
using UnityEngine;
using UnityEngine.SceneManagement;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR;

internal sealed class VRLoader : MonoBehaviour
{
    private OpenVRLoader loader;
    private bool started;
    private GameObject features;
    public static VRLoader Instance { get; private set; }
    public static VRLoader Create(bool isEnable)
    {
        if (!isEnable) return null;
        if (Instance != null) return Instance;
        var go = new GameObject("KKS VR Overhaul Loader");
        DontDestroyOnLoad(go);
        return Instance = go.AddComponent<VRLoader>();
    }

    private IEnumerator Start()
    {
        VRLog.Backend = new KksLogBackend();
        yield return new WaitUntil(() => Manager.Scene.initialized && SceneManager.GetActiveScene().name == "Studio");
        if (!TryStartRuntime()) yield break;
        float deadline = Time.realtimeSinceStartup + 30;
        while (SteamVR.initializedState == SteamVR.InitializedStates.Initializing && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (SteamVR.initializedState != SteamVR.InitializedStates.InitializeSuccess)
        {
            VRLog.Error("KKS SteamVR initialization failed or timed out: " + SteamVR.initializedState);
            ReleaseLoader();
            yield break;
        }

        try { InitializeFeatures(); }
        catch (Exception ex)
        {
            VRLog.Error("KKS VR feature initialization failed: " + ex);
            if (features != null) features.SetActive(false);
            // VRGIN has persistent camera objects and no supported shutdown API.
            // Do not stop tracking underneath an already-created VR rig.
            if (!VR.Active) ReleaseLoader();
            else VRLog.Error("KKS VR features disabled. Restart Studio after checking the error above; the XR runtime remains active until exit.");
        }
    }

    private bool TryStartRuntime()
    {
        try
        {
            KksOpenVrInputPatch.Install();
            KksInputStartup.InstallBeforeRuntime();
            var settings = OpenVRSettings.GetSettings(true);
            // KKS toon, hair, and the compiled ColorZOrder bundle have no
            // single-pass instanced variants. MultiPass draws each eye with the
            // ordinary view matrix, so those shaders stay in the correct eye.
            settings.StereoRenderingMode = OpenVRSettings.StereoRenderingModes.MultiPass;
            settings.InitializationType = OpenVRSettings.InitializationTypes.Scene;
            settings.EditorAppKey = "kss.charastudio.exe";
            SteamVR_Settings.instance.autoEnableVR = true;
            SteamVR_Settings.instance.editorAppKey = settings.EditorAppKey;
            loader = ScriptableObject.CreateInstance<OpenVRLoader>();
            if (!loader.Initialize()) throw new InvalidOperationException("OpenVR loader initialization returned false.");
            // Start can partially start subsystems before returning false or
            // throwing; attempt Stop during cleanup in either case.
            started = true;
            if (!loader.Start()) throw new InvalidOperationException("OpenVR loader start returned false.");
            SteamVR_Behaviour.Initialize(false);
            KksInputStartup.ActivateActionSet();
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Error("KKS XR startup failed. Check SteamVR and the KKS XR native dependencies: " + ex);
            ReleaseLoader();
            return false;
        }
    }

    private void InitializeFeatures()
    {
        Application.runInBackground = true;
        KksBreastPhysicsGuard.Install();
        SaveLoadSceneHook.InstallHook();
        LoadFixHook.InstallHook();
        DropdownFixHook.InstallHook();
        // VRSpawnPlacementHook is disabled to preserve Studio native default coordinates (world origin) for VMD alignment.
        KksCanvasCapture.Install();
        VRVisualPluginHooks.Install();
        VRMirrorFix.Install();
        VRManager.Create<KKCharaStudioInterpreter>(new ConfigurableContext());
        VRGameCompatibility.InstallGameplayCameraGuards();
        VR.Manager.SetMode<GenericStandingMode>();
        KksInputStartup.LogHands();
        var root = features = new GameObject("KKS VR Overhaul Features");
        DontDestroyOnLoad(root);
        IKTool.Create(root);
        VRControllerMgr.Install(root);
        VRCameraMoveHelper.Install(root);
        VRItemObjMoveHelper.Install(root);
        VRSpawnPlacementHelper.Install(root);
        root.AddComponent<DynamicBoneColliderManager>();
        root.AddComponent<KKCharaStudioVRGUI>();
        root.AddComponent<VRHandModelManager>();
        root.AddComponent<VRQuickActions>();
        root.AddComponent<VRMmdPlaybackController>();
        root.AddComponent<VRMmdCameraAnchorController>();
        root.AddComponent<VRWristMenuController>();
        root.AddComponent<VRTimelineCameraFollowController>();
        root.AddComponent<VRComfortVignette>();
        root.AddComponent<VRCameraPerformance>();
        root.AddComponent<VRTwoHandScale>();
        root.AddComponent<VRPhysicalUndresser>();
        DontDestroyOnLoad(VRCamera.Instance.gameObject);
        VRLog.Info("KKS VR Overhaul initialized with the KKS XR runtime.");
    }

    private void ReleaseLoader()
    {
        if (loader == null) return;
        var releasing = loader;
        bool stop = started;
        loader = null;
        started = false;
        try { if (stop) releasing.Stop(); }
        catch (Exception ex) { VRLog.Warn("KKS XR stop failed: " + ex.Message); }
        try { releasing.Deinitialize(); }
        catch (Exception ex) { VRLog.Warn("KKS XR deinitialization failed: " + ex.Message); }
        finally { Destroy(releasing); }
    }

    private void OnDestroy()
    {
        if (features != null) { features.SetActive(false); Destroy(features); }
        ReleaseLoader();
        if (Instance == this) Instance = null;
    }
}
