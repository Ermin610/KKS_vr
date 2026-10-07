using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Studio;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR;

internal static class VRInteractionOptions
{
    internal static KKCharaStudioVRSettings Settings
    {
        get
        {
            if (VR.Manager == null || VR.Manager.Context == null)
                return null;
            return VR.Manager.Context.Settings as KKCharaStudioVRSettings;
        }
    }

    internal static bool DynamicTouchEnabled
    {
        get
        {
            KKCharaStudioVRSettings settings = Settings;
            return settings == null || settings.DynamicTouchEnabled;
        }
    }

    internal static bool PhysicalUndressEnabled
    {
        get
        {
            KKCharaStudioVRSettings settings = Settings;
            return settings == null || settings.PhysicalUndressEnabled;
        }
    }

    internal static bool FigurePosingMode
    {
        get
        {
            KKCharaStudioVRSettings settings = Settings;
            return settings != null && settings.FigurePosingMode;
        }
    }

    internal static bool IkGuideVisible
    {
        get
        {
            KKCharaStudioVRSettings settings = Settings;
            return settings == null || settings.IkGuideVisible;
        }
    }
}

/// <summary>
/// When figure posing is on, a released IK guide keeps the pose it had at the
/// release instead of letting DynamicBone, the Animator, or the knead oscillator
/// pull it back. The pose is re-asserted every frame until posing is switched off
/// or the same guide is grabbed again.
/// </summary>
[DefaultExecutionOrder(30500)]
internal sealed class VRFigurePose : MonoBehaviour
{
    private const int MaxHeldPoses = 96;
    private const float PositionEpsilon = 0.0002f;
    private const float RotationEpsilon = 0.05f;

    private sealed class HeldPose
    {
        public GuideObject Guide;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public bool Committed;
    }

    private static VRFigurePose _instance;
    private readonly List<HeldPose> _held = new List<HeldPose>(16);
    private bool _commitFailureLogged;

    internal static bool IsEnabled => VRInteractionOptions.FigurePosingMode;

    internal static int HeldCount => _instance != null ? _instance._held.Count : 0;

    internal static void Schedule(GuideObject guide)
    {
        if (!IsEnabled || guide == null || guide.transformTarget == null)
            return;
        Ensure().Hold(guide);
        VRFigureScale.HoldReleasedRoot(guide);
    }

    /// <summary>
    /// Drops the hold on a guide a hand is about to move, so the lock cannot
    /// fight the drag.
    /// </summary>
    internal static void Forget(GuideObject guide)
    {
        if (_instance == null || guide == null)
            return;
        List<HeldPose> held = _instance._held;
        for (int i = held.Count - 1; i >= 0; i--)
        {
            if (held[i].Guide == guide)
                held.RemoveAt(i);
        }
    }

    /// <summary>
    /// Drops every hold on <paramref name="guides"/> and returns how many were
    /// released.
    /// </summary>
    internal static int ForgetAll(HashSet<GuideObject> guides)
    {
        if (_instance == null || guides == null || guides.Count == 0)
            return 0;
        return _instance._held.RemoveAll(pose => pose.Guide != null && guides.Contains(pose.Guide));
    }

    private static VRFigurePose Ensure()
    {
        if (_instance != null)
            return _instance;
        GameObject host = new GameObject("VRFigurePose");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<VRFigurePose>();
        return _instance;
    }

    private void Hold(GuideObject guide)
    {
        Transform bone = guide.transformTarget;
        for (int i = 0; i < _held.Count; i++)
        {
            if (_held[i].Guide != guide)
                continue;
            _held[i].LocalPosition = bone.localPosition;
            _held[i].LocalRotation = bone.localRotation;
            _held[i].Committed = false;
            return;
        }
        if (_held.Count >= MaxHeldPoses)
            _held.RemoveAt(0);
        _held.Add(new HeldPose
        {
            Guide = guide,
            LocalPosition = bone.localPosition,
            LocalRotation = bone.localRotation
        });
        VRIkPosing.FreezeAnimationIfPosing(guide);
    }

    private void LateUpdate()
    {
        if (_held.Count == 0)
            return;
        if (!IsEnabled)
        {
            _held.Clear();
            return;
        }

        for (int i = _held.Count - 1; i >= 0; i--)
        {
            HeldPose pose = _held[i];
            if (pose.Guide == null || pose.Guide.transformTarget == null || pose.Guide.changeAmount == null)
            {
                _held.RemoveAt(i);
                continue;
            }
            Apply(pose);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Apply(HeldPose pose)
    {
        GuideObject guide = pose.Guide;
        Transform bone = guide.transformTarget;
        bool drifted = !pose.Committed;

        if (guide.enablePos && IsFinite(pose.LocalPosition)
            && (bone.localPosition - pose.LocalPosition).sqrMagnitude > PositionEpsilon * PositionEpsilon)
        {
            bone.localPosition = pose.LocalPosition;
            drifted = true;
        }
        if (guide.enableRot && IsFinite(pose.LocalRotation)
            && Quaternion.Angle(bone.localRotation, pose.LocalRotation) > RotationEpsilon)
        {
            bone.localRotation = pose.LocalRotation;
            drifted = true;
        }
        if (!drifted)
            return;

        try
        {
            if (guide.enablePos && IsFinite(bone.localPosition))
                guide.changeAmount.pos = bone.localPosition;
            if (guide.enableRot && IsFinite(bone.localEulerAngles))
            {
                guide.changeAmount.rot = UnwrapEuler(
                    guide.changeAmount.rot,
                    bone.localEulerAngles);
            }
            guide.changeAmount.OnChange();
            guide.ForceUpdate();
        }
        catch (Exception ex)
        {
            if (!_commitFailureLogged)
            {
                _commitFailureLogged = true;
                VRLog.Warn("Figure pose commit failed: " + ex.Message);
            }
        }

        if (pose.Committed)
            return;
        pose.Committed = true;
        // The rest pose bake walks every DynamicBone on the character, so it only
        // runs on the frame the pose is first locked, not on every re-assert.
        VRDynamicBoneRest.BakeChain(bone);
    }

    private static Vector3 UnwrapEuler(Vector3 previous, Vector3 current)
    {
        if (!IsFinite(previous))
            return current;
        current.x = previous.x + Mathf.DeltaAngle(previous.x, current.x);
        current.y = previous.y + Mathf.DeltaAngle(previous.y, current.y);
        current.z = previous.z + Mathf.DeltaAngle(previous.z, current.z);
        return current;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        if (!IsFinite(new Vector3(value.x, value.y, value.z))
            || float.IsNaN(value.w) || float.IsInfinity(value.w))
            return false;
        float mag = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        return mag > 0.0001f && mag < 4f;
    }
}

internal static class VRDynamicBoneRest
{
    private sealed class Layout
    {
        public FieldInfo Particles;
        public FieldInfo Root;
        public FieldInfo Weight;
        public FieldInfo Position;
        public FieldInfo Previous;
        public FieldInfo Transform;
        public FieldInfo InitLocalPosition;
        public FieldInfo InitLocalRotation;
    }

    private static readonly Dictionary<Type, Layout> Layouts = new Dictionary<Type, Layout>();

    internal static void BakeChain(Transform bone)
    {
        if (bone == null)
            return;
        ChaControl character = bone.GetComponentInParent<ChaControl>();
        if (character == null)
            return;

        MonoBehaviour[] behaviours = character.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;
            string typeName = behaviour.GetType().Name;
            if (typeName.IndexOf("DynamicBone", StringComparison.Ordinal) < 0
                || typeName.IndexOf("Collider", StringComparison.Ordinal) >= 0)
                continue;
            Layout layout = GetLayout(behaviour.GetType());
            if (layout.Root == null || layout.Particles == null)
                continue;
            Transform root = layout.Root.GetValue(behaviour) as Transform;
            if (root == null)
                continue;
            if (root != bone && !root.IsChildOf(bone) && !bone.IsChildOf(root))
                continue;
            Bake(behaviour, layout);
        }
    }

    internal static void Bake(MonoBehaviour dynamicBone)
    {
        if (dynamicBone == null)
            return;
        Bake(dynamicBone, GetLayout(dynamicBone.GetType()));
    }

    private static void Bake(MonoBehaviour dynamicBone, Layout layout)
    {
        if (layout.Particles == null)
            return;
        try
        {
            IList particles = layout.Particles.GetValue(dynamicBone) as IList;
            if (particles == null)
                return;
            for (int i = 0; i < particles.Count; i++)
            {
                object particle = particles[i];
                if (particle == null || layout.Transform == null)
                    continue;
                Transform particleTransform = layout.Transform.GetValue(particle) as Transform;
                if (particleTransform == null)
                    continue;
                if (layout.InitLocalPosition != null)
                    layout.InitLocalPosition.SetValue(particle, particleTransform.localPosition);
                if (layout.InitLocalRotation != null)
                    layout.InitLocalRotation.SetValue(particle, particleTransform.localRotation);
                if (layout.Position != null)
                    layout.Position.SetValue(particle, particleTransform.position);
                if (layout.Previous != null)
                    layout.Previous.SetValue(particle, particleTransform.position);
            }

            if (layout.Weight != null)
            {
                object weight = layout.Weight.GetValue(dynamicBone);
                if (weight is float value && value < 0.01f)
                    layout.Weight.SetValue(dynamicBone, 1f);
            }
        }
        catch (Exception ex)
        {
            VRLog.Warn("DynamicBone rest pose bake skipped: " + ex.Message);
        }
    }

    private static Layout GetLayout(Type type)
    {
        Layout layout;
        if (Layouts.TryGetValue(type, out layout))
            return layout;
        layout = new Layout();
        layout.Particles = Find(type, "m_Particles", "Particles");
        layout.Root = Find(type, "m_Root", "Root");
        layout.Weight = Find(type, "m_Weight", "Weight");
        if (layout.Particles != null && layout.Particles.FieldType.IsGenericType)
        {
            Type particleType = layout.Particles.FieldType.GetGenericArguments()[0];
            layout.Position = Find(particleType, "m_Position", "Position");
            layout.Previous = Find(particleType, "m_PrevPosition", "PrevPosition");
            layout.Transform = Find(particleType, "m_Transform", "Transform");
            layout.InitLocalPosition = Find(particleType, "m_InitLocalPosition", "InitLocalPosition");
            layout.InitLocalRotation = Find(particleType, "m_InitLocalRotation", "InitLocalRotation");
        }
        Layouts[type] = layout;
        return layout;
    }

    private static FieldInfo Find(Type type, string primary, string fallback)
    {
        Type current = type;
        while (current != null)
        {
            FieldInfo field = current.GetField(primary, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? current.GetField(fallback, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field;
            current = current.BaseType;
        }
        return null;
    }
}
