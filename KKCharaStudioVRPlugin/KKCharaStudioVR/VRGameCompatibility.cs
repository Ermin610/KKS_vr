using System.Collections.Generic;
using UnityEngine;
using VRGIN.Controls;

namespace KKCharaStudioVR;

internal static class VRGameCompatibility
{
#if KKS
    internal const string PluginAssemblyName = "KKSCharaStudioVRPlugin";
    internal const string CameraSyncAssemblyName = "KKS_VR_CameraSync";
    internal const string CharacterApiTypeName = "KKAPI.Chara.CharacterApi, KKSAPI";
    internal const string AccessoryStatesAssemblyName = "KKS_Accessory_States";
    internal const string CoordinateAssemblyName = "KKS_CoordinateLoadOption";
    internal const string CoordinateNamespace = "CoordinateLoadOption.";
    internal const string CoordinatePluginTypeName = "CoordinateLoadOption.CoordinateLoadOption";
    internal const string BrowserFoldersAssemblyName = "KKS_BrowserFolders";
    internal static bool IsLoading => Manager.Scene.IsNowLoading || Manager.Scene.IsNowLoadingFade;
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
    internal static int DeviceIndex(Controller controller) => controller.Tracking.GetDeviceIndex();
#else
    internal const string PluginAssemblyName = "KKCharaStudioVRPlugin";
    internal const string CameraSyncAssemblyName = "KK_VR_CameraSync";
    internal const string CharacterApiTypeName = "KKAPI.Chara.CharacterApi, KKAPI";
    internal const string AccessoryStatesAssemblyName = "KK_Accessory_States";
    internal const string CoordinateAssemblyName = "KK_CoordinateLoadOption";
    internal const string CoordinateNamespace = "KK_CoordinateLoadOption.";
    internal const string CoordinatePluginTypeName = "KK_CoordinateLoadOption.KK_CoordinateLoadOption";
    internal const string BrowserFoldersAssemblyName = "KK_BrowserFolders";
    internal static bool IsLoading => Singleton<Manager.Scene>.Instance != null &&
        (Singleton<Manager.Scene>.Instance.IsNowLoading || Singleton<Manager.Scene>.Instance.IsNowLoadingFade);
    internal static IEnumerable<ChaControl> Characters => Singleton<Manager.Character>.Instance.dictEntryChara.Values;
    internal static ChaListControl CharacterList => Singleton<Manager.Character>.Instance?.chaListCtrl;
    internal static void DeleteCharacter(ChaControl character) => Singleton<Manager.Character>.Instance.DeleteChara(character, false);
    internal static void SaveAudioConfig() { var config = Singleton<Manager.Config>.Instance; if (config != null) config.Save(); }
    internal static void CaptureScreenshot(string path) => Application.CaptureScreenshot(path);
    internal static int DeviceIndex(Controller controller) => (int)controller.Tracking.index;
#endif
}
