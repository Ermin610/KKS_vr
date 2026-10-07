using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using VRGIN.Core;

namespace KKCharaStudioVR;

// BreastPhysicsController.ParamChara.Clone copies every saved coordinate into a
// freshly constructed ParamChara. KKS only defines Plain/Swim/Pajamas/Bathing
// (0-3). A default-status file saved with KK's seven coordinates (0-6) misses
// keys 4-6, Clone throws, and LateUpdate never sets endInitLoad, so the
// exception repeats for every female character every frame.
internal static class KksBreastPhysicsGuard
{
    static bool installed;
    static bool loggedSkip;
    static bool loggedGiveUp;
    static FieldInfo endInitLoad;

    public static void Install()
    {
        if (installed)
            return;
        Type chara = AccessTools.TypeByName("BreastPhysicsController.ParamChara");
        MethodInfo clone = chara == null ? null : AccessTools.Method(chara, "Clone");
        if (clone == null)
        {
            VRLog.Info("BreastPhysicsController is not loaded; coordinate guard stays off.");
            return;
        }

        installed = true;
        var harmony = new Harmony("KKCharaStudioVR.BreastPhysicsGuard");
        harmony.Patch(clone, prefix: new HarmonyMethod(typeof(KksBreastPhysicsGuard), nameof(ClonePrefix)));
        Type controller = AccessTools.TypeByName("BreastPhysicsController.ParamCharaController");
        MethodInfo late = controller == null ? null : AccessTools.Method(controller, "LateUpdate");
        if (late != null)
        {
            endInitLoad = AccessTools.Field(controller, "endInitLoad");
            harmony.Patch(late, finalizer: new HarmonyMethod(typeof(KksBreastPhysicsGuard), nameof(LateUpdateFinalizer)));
        }
        VRLog.Info("BreastPhysicsController.Clone skips coordinate keys this KKS build does not define.");
    }

    static bool ClonePrefix(object __instance, ref object __result)
    {
        try
        {
            __result = SafeClone(__instance);
            return false;
        }
        catch (Exception ex)
        {
            VRLog.Warn("BreastPhysics safe clone failed; original Clone will run: " + ex.Message);
            return true;
        }
    }

    static Exception LateUpdateFinalizer(object __instance, Exception __exception)
    {
        if (__exception == null)
            return null;
        Exception root = __exception is TargetInvocationException tie && tie.InnerException != null
            ? tie.InnerException
            : __exception;
        if (!(root is KeyNotFoundException))
            return __exception;
        if (endInitLoad != null && __instance != null)
            endInitLoad.SetValue(__instance, true);
        if (!loggedGiveUp)
        {
            loggedGiveUp = true;
            VRLog.Warn("BreastPhysicsController.LateUpdate hit a missing dictionary key. Init is marked finished so it does not throw every frame: " + root.Message);
        }
        return null;
    }

    internal static object SafeClone(object src)
    {
        Type type = src.GetType();
        object dst = Activator.CreateInstance(type);
        CopyClonedField(type, src, dst, "paramBustNaked");
        int skipped = CopyOverlappingMaps(
            type.GetField("paramBust")?.GetValue(src) as IDictionary,
            type.GetField("paramBust")?.GetValue(dst) as IDictionary);
        CopyClonedField(type, src, dst, "paramHip");
        if (skipped > 0 && !loggedSkip)
        {
            loggedSkip = true;
            VRLog.Warn(
                "BreastPhysics default status has "
                + skipped
                + " coordinate entries this KKS build does not use (saved keys include KK's 0-6; KKS ParamChara only has Plain/Swim/Pajamas/Bathing). Those entries are skipped.");
        }
        return dst;
    }

    static void CopyClonedField(Type type, object src, object dst, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object value = field?.GetValue(src);
        if (field == null || value == null)
            return;
        MethodInfo clone = value.GetType().GetMethod("Clone", Type.EmptyTypes);
        if (clone == null)
            return;
        field.SetValue(dst, clone.Invoke(value, null));
    }

    static int CopyOverlappingMaps(IDictionary source, IDictionary destination)
    {
        if (source == null || destination == null)
            return 0;
        int skipped = 0;
        foreach (DictionaryEntry coordinate in source)
        {
            if (!destination.Contains(coordinate.Key) || !(coordinate.Value is IDictionary sourceWear))
            {
                skipped++;
                continue;
            }
            if (!(destination[coordinate.Key] is IDictionary destinationWear))
            {
                skipped++;
                continue;
            }
            foreach (DictionaryEntry wear in sourceWear)
            {
                if (!destinationWear.Contains(wear.Key) || wear.Value == null)
                {
                    skipped++;
                    continue;
                }
                MethodInfo clone = wear.Value.GetType().GetMethod("Clone", Type.EmptyTypes);
                if (clone == null)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    destinationWear[wear.Key] = clone.Invoke(wear.Value, null);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is KeyNotFoundException)
                {
                    skipped++;
                }
            }
        }
        return skipped;
    }
}
