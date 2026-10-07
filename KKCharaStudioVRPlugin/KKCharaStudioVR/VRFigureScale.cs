using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR;

/// <summary>
/// Scales a whole character through its root guide so Studio, undo, and scene
/// saves all see the change. Requests glide to the target; in figure posing
/// mode the reached scale is re-asserted like the held pose, until the same
/// guide is grabbed again.
/// </summary>
[DefaultExecutionOrder(30400)]
internal sealed class VRFigureScale : MonoBehaviour
{
    private sealed class Entry
    {
        public OCIChar Character;
        public GuideObject Guide;
        public Vector3 Ratio = Vector3.one;
        public float Current;
        public float Target;
        public bool Settled;
        public bool UndoPending;
        public Vector3 UndoFrom;
    }

    private static VRFigureScale _instance;
    private readonly List<Entry> _entries = new List<Entry>(4);
    private bool _writeFailureLogged;

    internal static bool TryGetScale(OCIChar character, out float scale)
    {
        scale = 1f;
        GuideObject guide = GuideOf(character);
        if (guide == null)
            return false;
        Entry entry = _instance != null ? _instance.Find(guide) : null;
        scale = entry != null ? entry.Target : VRFigureScalePolicy.Clamp(guide.changeAmount.scale.x);
        return true;
    }

    internal static bool Request(OCIChar character, float scale, out string status)
    {
        GuideObject guide = GuideOf(character);
        if (guide == null)
        {
            status = "所选角色尚未初始化完成或已经失效";
            return false;
        }
        if (!guide.enableScale)
        {
            status = VRCharacterClothingService.GetCharacterName(character) + "：该角色不支持缩放";
            return false;
        }

        VRFigureScale driver = Ensure();
        Entry entry = driver.Find(guide) ?? driver.Add(character, guide);
        if (!entry.UndoPending)
        {
            entry.UndoPending = true;
            entry.UndoFrom = guide.changeAmount.scale;
        }
        entry.Target = VRFigureScalePolicy.Clamp(scale);
        entry.Settled = false;
        status = VRCharacterClothingService.GetCharacterName(character)
            + "：缩放 " + VRFigureScalePolicy.FormatRatio(entry.Target)
            + " (" + entry.Target.ToString("0.000") + "x)";
        return true;
    }

    /// <summary>
    /// Records one undo step from the scale before the first request to the
    /// target. A slider drag commits once on release, not on every frame.
    /// </summary>
    internal static void CommitEdit(OCIChar character)
    {
        GuideObject guide = GuideOf(character);
        Entry entry = guide != null && _instance != null ? _instance.Find(guide) : null;
        if (entry == null || !entry.UndoPending)
            return;
        entry.UndoPending = false;
        Vector3 to = entry.Ratio * entry.Target;
        if ((to - entry.UndoFrom).sqrMagnitude < 1e-10f)
            return;
        try
        {
            if (Singleton<UndoRedoManager>.Instance == null)
                return;
            GuideCommand.EqualsInfo[] infos = new GuideCommand.EqualsInfo[1]
            {
                new GuideCommand.EqualsInfo
                {
                    dicKey = guide.dicKey,
                    oldValue = entry.UndoFrom,
                    newValue = to
                }
            };
            Singleton<UndoRedoManager>.Instance.Push(new GuideCommand.ScaleEqualsCommand(infos));
        }
        catch (Exception ex)
        {
            VRLog.Warn("Figure scale undo skipped: " + ex.Message);
        }
    }

    /// <summary>
    /// A hand is about to move this guide (including its scale handle). Drop the
    /// lock so it cannot fight the drag.
    /// </summary>
    internal static void Forget(GuideObject guide)
    {
        if (_instance == null || guide == null)
            return;
        for (int i = _instance._entries.Count - 1; i >= 0; i--)
        {
            if (_instance._entries[i].Guide == guide)
                _instance._entries.RemoveAt(i);
        }
    }

    /// <summary>
    /// A character root guide released in figure posing mode keeps the scale it
    /// was released with, the same way a limb keeps its pose.
    /// </summary>
    internal static void HoldReleasedRoot(GuideObject guide)
    {
        if (guide == null || guide.changeAmount == null || !VRInteractionOptions.FigurePosingMode)
            return;
        OCIChar character = FindCharacterByRootGuide(guide);
        if (character == null || !guide.enableScale)
            return;
        VRFigureScale driver = Ensure();
        Entry entry = driver.Find(guide) ?? driver.Add(character, guide);
        entry.Target = VRFigureScalePolicy.Clamp(guide.changeAmount.scale.x);
        entry.Current = entry.Target;
        entry.Settled = true;
    }

    private static GuideObject GuideOf(OCIChar character)
    {
        try
        {
            if (character == null || character.charInfo == null)
                return null;
            GuideObject guide = character.guideObject;
            return guide != null && guide.changeAmount != null ? guide : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static OCIChar FindCharacterByRootGuide(GuideObject guide)
    {
        try
        {
            Studio.Studio studio = Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.dicObjectCtrl == null)
                return null;
            foreach (KeyValuePair<int, ObjectCtrlInfo> pair in studio.dicObjectCtrl)
            {
                OCIChar character = pair.Value as OCIChar;
                if (character != null && character.guideObject == guide)
                    return character;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static VRFigureScale Ensure()
    {
        if (_instance != null)
            return _instance;
        GameObject host = new GameObject("VRFigureScale");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<VRFigureScale>();
        return _instance;
    }

    private Entry Find(GuideObject guide)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Guide == guide)
                return _entries[i];
        }
        return null;
    }

    private Entry Add(OCIChar character, GuideObject guide)
    {
        Vector3 scale = guide.changeAmount.scale;
        float baseScale = scale.x;
        Entry entry = new Entry
        {
            Character = character,
            Guide = guide,
            Ratio = Mathf.Abs(baseScale) > 0.0001f && IsFinite(scale) ? scale / baseScale : Vector3.one,
            Current = VRFigureScalePolicy.Clamp(baseScale),
            Settled = true
        };
        entry.Target = entry.Current;
        _entries.Add(entry);
        return entry;
    }

    private void LateUpdate()
    {
        if (_entries.Count == 0)
            return;
        bool posing = VRInteractionOptions.FigurePosingMode;
        float deltaTime = Time.unscaledDeltaTime;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            if (GuideOf(entry.Character) != entry.Guide)
            {
                _entries.RemoveAt(i);
                continue;
            }

            if (!entry.Settled)
            {
                entry.Current = VRFigureScalePolicy.Approach(entry.Current, entry.Target, deltaTime);
                entry.Settled = entry.Current == entry.Target;
                Write(entry, entry.Current);
                continue;
            }

            if (!posing)
            {
                if (!entry.UndoPending)
                    _entries.RemoveAt(i);
                continue;
            }

            if (!VRFigureScalePolicy.IsSettled(entry.Guide.changeAmount.scale.x, entry.Target))
                Write(entry, entry.Target);
        }
    }

    private void Write(Entry entry, float value)
    {
        try
        {
            GuideObject guide = entry.Guide;
            guide.changeAmount.scale = entry.Ratio * value;
            guide.changeAmount.OnChange();
            guide.ForceUpdate();
        }
        catch (Exception ex)
        {
            if (_writeFailureLogged)
                return;
            _writeFailureLogged = true;
            VRLog.Warn("Figure scale write failed: " + ex.Message);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
