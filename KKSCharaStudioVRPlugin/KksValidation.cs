using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGIN.Core;

namespace KKCharaStudioVR;

// Explicit automated smoke-test mode. It only inspects the empty Studio and
// exits the process started by the validation script; it never loads/saves cards.
internal sealed class KksValidation : MonoBehaviour
{
    private readonly List<string> results = new();
    private bool failed;
    private IEnumerator Start()
    {
        float deadline = Time.realtimeSinceStartup + 180;
        while ((!Manager.Scene.initialized || SceneManager.GetActiveScene().name != "Studio") && Time.realtimeSinceStartup < deadline)
            yield return null;
        Check("Studio initialized", () => Manager.Scene.initialized && SceneManager.GetActiveScene().name == "Studio");
        results.Add("Unity=" + Application.unityVersion);
        Check("NoVR creates no VR rig", () => !VR.Active && VRLoader.Instance == null);
        Check("Settings XML round trip", () => {
            var serializer = new XmlSerializer(typeof(KKCharaStudioVRSettings));
            var settings = new KKCharaStudioVRSettings { TimelineVerticalOffset = 1.25f, WristMenuLanguage = "zh-CN" };
            using (var writer = new StringWriter()) {
                serializer.Serialize(writer, settings);
                var restored = (KKCharaStudioVRSettings)serializer.Deserialize(new StringReader(writer.ToString()));
                return restored.TimelineVerticalOffset == 1.25f && restored.WristMenuLanguage == "zh-CN";
            }
        });
        Check("KKS hand shader loads", () => { var shader = MaterialHelper.GetColorZOrderShader(); return shader != null && shader.isSupported; });
        Check("KKSAPI character API resolves", () => Type.GetType(VRGameCompatibility.CharacterApiTypeName, false) != null);
        Check("KKS accessory states resolves", () => Type.GetType("Accessory_States.CharaEvent, " + VRGameCompatibility.AccessoryStatesAssemblyName, false) != null);
        Check("KKS coordinate adapter types resolve", () => {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == VRGameCompatibility.CoordinateAssemblyName);
            return assembly.GetType(VRGameCompatibility.CoordinatePluginTypeName) != null && assembly.GetType(VRGameCompatibility.CoordinateNamespace + "CoordinateLoad") != null;
        });
        Check("Core Harmony patches install", () => {
            SaveLoadSceneHook.InstallHook();
            LoadFixHook.InstallHook();
            DropdownFixHook.InstallHook();
            KksCanvasCapture.Install();
            KksOpenVrInputPatch.Install();
            var method = typeof(Studio.Studio).GetMethod("LoadScene", new[] { typeof(string) });
            return HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Contains("KKChacaStudioVR.LoadFixHook") == true;
        });
        Check("CameraSync wrist contract resolves", () => {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == VRGameCompatibility.CameraSyncAssemblyName);
            var type = assembly.GetType("KK_VR_CameraSync.CameraSyncDriver", true);
            return new[] { "Suspend", "ResumeAndReset", "ResumeAndPrimeTimelineState", "SetTimelineCameraSuppressed", "SetTimelineFovCompensationOverride", "SetTimelineUserPoseOffset", "SetExternalVrCameraOwner", "IsTimelineCameraWriterActive" }
                .All(name => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic) != null);
        });
        Check("Timeline anchor, offsets and tracking math", () => {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == VRGameCompatibility.CameraSyncAssemblyName);
            return (bool)assembly.GetType("KK_VR_CameraSync.CameraSyncDriver", true)
                .GetMethod("ValidateTimelinePoseMath", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        });
        Check("MMDD CLR assembly name resolves", () => Assembly.Load(VRGameCompatibility.PluginAssemblyName) == typeof(KksValidation).Assembly);
        results.Add(failed ? "RESULT=FAIL" : "RESULT=PASS");
        string report = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--kksvr-report=", StringComparison.Ordinal));
        if (report != null) File.WriteAllLines(report.Substring("--kksvr-report=".Length), results.ToArray());
        foreach (string line in results) BepInEx4.Logger.Log(BepInEx.Logging.LogLevel.Info, "[KKS validation] " + line);
        Application.Quit(failed ? 1 : 0);
    }
    private void Check(string name, Func<bool> test)
    {
        try { if (test()) { results.Add("PASS " + name); return; } results.Add("FAIL " + name); }
        catch (Exception ex) { results.Add("FAIL " + name + ": " + ex); }
        failed = true;
    }
}
