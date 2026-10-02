using BepInEx.Logging;
using VRGIN.Core;

namespace KKCharaStudioVR;
internal sealed class KksLogBackend : ILoggerBackend
{
    private readonly ManualLogSource source = BepInEx.Logging.Logger.CreateLogSource("KKS VR");
    public void Log(string text, object[] args, VRLog.LogMode severity)
    {
        var level = severity == VRLog.LogMode.Error ? LogLevel.Error : severity == VRLog.LogMode.Warning ? LogLevel.Warning
            : severity == VRLog.LogMode.Debug ? LogLevel.Debug : LogLevel.Info;
        source.Log(level, args == null || args.Length == 0 ? text : string.Format(text, args));
    }
}
