using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Manager;
using UnityEngine;
using Object = UnityEngine.Object;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    [DefaultExecutionOrder(-5000)]
    public class DynamicBoneColliderManager : MonoBehaviour
    {
        private class ColliderContext
        {
            public DynamicBoneCollider collider;
            public Transform controllerRoot;
            public bool isLeft;
            public int fingerIndex;
            public Vector3 defaultLocalPos;
        }

        private KKCharaStudioVRSettings _settings;
        private List<DynamicBoneCollider> _handColliders = new List<DynamicBoneCollider>();
        private List<ColliderContext> _colliderContexts = new List<ColliderContext>();

        // Use a generic HashSet<MonoBehaviour> to store registered bones to avoid static compile-time type dependency
        private HashSet<MonoBehaviour> _registeredBonesReflection = new HashSet<MonoBehaviour>();

        public void Start()
        {
            if (VR.Manager != null && VR.Manager.Context != null)
            {
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
            }
            StartCoroutine(InitCollidersCo());
            if (GetComponent<VRDynamicBoneGuard>() == null)
                gameObject.AddComponent<VRDynamicBoneGuard>();
            if (GetComponent<VRPhysicsKneadManager>() == null)
                gameObject.AddComponent<VRPhysicsKneadManager>();
        }

        private IEnumerator InitCollidersCo()
        {
            while (VR.Mode == null || VR.Mode.Left == null || VR.Mode.Right == null)
            {
                yield return new WaitForSeconds(0.5f);
            }

            while (VRHandModelManager.Instance == null)
            {
                yield return new WaitForSeconds(0.5f);
            }

            CreateHandColliders(VR.Mode.Left.transform, true);
            CreateHandColliders(VR.Mode.Right.transform, false);
            VRHandCollision.Refresh();

            StartCoroutine(ScanDynamicBonesCo());
        }

        private void CreateHandColliders(Transform controllerRoot, bool isLeft)
        {
            string prefix = isLeft ? "L_" : "R_";
            string[] parts = { "Palm", "Thumb", "Index", "Middle", "Ring", "Little" };

            // Aligned with VRHandModelManager finger tip positions (mirror = isLeft? -1:1)
            float m = isLeft ? -1f : 1f;
            Vector3[] positions = new Vector3[]
            {
                new Vector3(0f, -0.03f, 0.04f),                        // Palm center
                new Vector3(0.04f * m, -0.025f, 0.065f),               // Thumb tip
                new Vector3(0.025f * m, -0.03f, 0.145f),               // Index tip (3 seg × 0.020)
                new Vector3(0.008f * m, -0.03f, 0.156f),               // Middle tip (3 seg × 0.022)
                new Vector3(-0.010f * m, -0.03f, 0.145f),              // Ring tip
                new Vector3(-0.028f * m, -0.03f, 0.126f),              // Little tip
            };

            var touchHandler = controllerRoot.GetComponent<VRPhysicalTouchHandler>();
            if (touchHandler == null)
            {
                touchHandler = controllerRoot.gameObject.AddComponent<VRPhysicalTouchHandler>();
                touchHandler.isLeftHand = isLeft;
                touchHandler.trackedObject = controllerRoot.GetComponent<SteamVR_TrackedObject>();
            }

            for (int i = 0; i < parts.Length; i++)
            {
                GameObject obj = new GameObject(prefix + parts[i] + "_Col");

                int fingerIndex = i - 1;
                // Parent to an unscaled anchor. DynamicBoneCollider multiplies
                // m_Radius by lossyScale.z, so a fingertip or a scaled VR rig
                // turns a 2 cm sphere into a limb-sized one and the hair explodes.
                obj.transform.SetParent(VRHandModelManager.PhysicsAnchor(), false);
                obj.transform.localRotation = Quaternion.identity;
                obj.transform.localScale = Vector3.one;

                var dbc = obj.AddComponent<DynamicBoneCollider>();
                dbc.m_Radius = ClampedRadius();
                dbc.m_Height = 0f;
                dbc.m_Bound = DynamicBoneCollider.Bound.Outside;
                dbc.m_Center = Vector3.zero;
                _handColliders.Add(dbc);

                ColliderContext ctx = new ColliderContext
                {
                    collider = dbc,
                    controllerRoot = controllerRoot,
                    isLeft = isLeft,
                    fingerIndex = fingerIndex,
                    defaultLocalPos = positions[i]
                };
                _colliderContexts.Add(ctx);
                obj.transform.position = FollowPoint(ctx);

                var sphere = obj.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = dbc.m_Radius;

                var rb = obj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var trigger = obj.AddComponent<VRHandHapticTrigger>();
                var tracked = controllerRoot.GetComponent<SteamVR_TrackedObject>();
                trigger.trackedObject = tracked;
                trigger.isLeftHand = isLeft;

                var contactProxy = obj.AddComponent<VRHandContactProxy>();
                contactProxy.touchHandler = touchHandler;
                contactProxy.isLeftHand = isLeft;
                contactProxy.fingerIndex = fingerIndex;

                // Unity trigger for haptics only. DynamicBoneCollider above is
                // what actually pushes breasts, hair, and skirts.
                VRHandCollision.AdoptProxy(obj);
            }
        }

        private void OnLevelWasLoaded(int level)
        {
            lock (_registeredBonesReflection)
            {
                _registeredBonesReflection.Clear();
            }
        }

        private void OnDestroy()
        {
            lock (_registeredBonesReflection)
            {
                foreach (var bone in _registeredBonesReflection)
                {
                    if (bone == null) continue;
                    try
                    {
                        Type type = bone.GetType();
                        var field = type.GetField("m_Colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? type.GetField("Colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (field != null)
                        {
                            var list = field.GetValue(bone) as IList;
                            if (list != null)
                            {
                                foreach (var hc in _handColliders)
                                {
                                    list.Remove(hc);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        VRLog.Warn("Failed to unregister collider via reflection: " + ex.Message);
                    }
                }
                _registeredBonesReflection.Clear();
            }
        }

        private void LateUpdate()
        {
            bool want = _settings == null || _settings.DynamicBoneCollisionEnabled;
            float radius = ClampedRadius();
            const float maxStep = 0.08f;
            for (int i = 0; i < _colliderContexts.Count; i++)
            {
                ColliderContext ctx = _colliderContexts[i];
                if (ctx == null || ctx.collider == null) continue;
                DynamicBoneCollider dbc = ctx.collider;
                Transform bone = dbc.transform;
                bone.localScale = Vector3.one;
                dbc.m_Radius = radius;
                dbc.m_Height = 0f;
                dbc.m_Center = Vector3.zero;
                dbc.m_Bound = DynamicBoneCollider.Bound.Outside;

                SphereCollider sphere = dbc.GetComponent<SphereCollider>();
                if (sphere != null) sphere.radius = radius;
                VRHandHapticTrigger trigger = dbc.GetComponent<VRHandHapticTrigger>();
                if (trigger != null) trigger.enabled = want;

                Vector3 target = FollowPoint(ctx);
                if (!IsFinite(target))
                {
                    dbc.enabled = false;
                    continue;
                }
                Vector3 current = bone.position;
                bool jumped = !IsFinite(current) || (target - current).sqrMagnitude > maxStep * maxStep;
                bone.position = target;
                bone.rotation = Quaternion.identity;
                // A teleported sphere projects every particle inside it to the
                // surface in one step. Leave it disabled for that frame.
                dbc.enabled = want && !jumped;
            }
        }

        private float ClampedRadius()
        {
            float radius = _settings != null ? _settings.ColliderRadius : 0.02f;
            if (float.IsNaN(radius) || float.IsInfinity(radius)) return 0.02f;
            return Mathf.Clamp(radius, 0.005f, 0.1f);
        }

        private static Vector3 FollowPoint(ColliderContext ctx)
        {
            if (ctx.fingerIndex >= 0 && VRHandModelManager.Instance != null)
            {
                Transform tip = VRHandModelManager.Instance.GetFingerTipTransform(ctx.isLeft, ctx.fingerIndex);
                if (tip != null && tip.gameObject.activeInHierarchy && IsFinite(tip.position))
                    return tip.position;
            }
            if (ctx.controllerRoot != null)
            {
                Vector3 world = ctx.controllerRoot.TransformPoint(ctx.defaultLocalPos);
                if (IsFinite(world)) return world;
            }
            return ctx.collider != null ? ctx.collider.transform.position : Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private IEnumerator ScanDynamicBonesCo()
        {
            while (true)
            {
                yield return new WaitForSeconds(1.5f);

                if (_settings != null && !_settings.DynamicBoneCollisionEnabled)
                {
                    continue;
                }

                lock (_registeredBonesReflection)
                {
                    // Clean up null entries
                    var toRemove = new List<MonoBehaviour>();
                    foreach (var b in _registeredBonesReflection)
                    {
                        if (b == null) toRemove.Add(b);
                    }
                    foreach (var b in toRemove)
                    {
                        _registeredBonesReflection.Remove(b);
                    }
                }

                var allChas = FindObjectsOfType<ChaControl>();
                foreach (var chaCtrl in allChas)
                {
                    if (chaCtrl != null && chaCtrl.objBodyBone != null)
                    {
                        // Retrieve all MonoBehaviour scripts attached to character children
                        var behaviours = chaCtrl.GetComponentsInChildren<MonoBehaviour>(true);
                        foreach (var b in behaviours)
                        {
                            if (b == null) continue;

                            string typeName = b.GetType().Name;
                            if (typeName.Contains("DynamicBone") && !typeName.Contains("Collider"))
                            {
                                bool alreadyRegistered = false;
                                lock (_registeredBonesReflection)
                                {
                                    alreadyRegistered = _registeredBonesReflection.Contains(b);
                                }

                                if (alreadyRegistered && !HandCollidersStillAttached(b))
                                {
                                    lock (_registeredBonesReflection)
                                    {
                                        _registeredBonesReflection.Remove(b);
                                    }
                                    alreadyRegistered = false;
                                }

                                if (!alreadyRegistered)
                                {
                                    RegisterCollidersReflection(b);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void RegisterCollidersReflection(MonoBehaviour bone)
        {
            try
            {
                Type type = bone.GetType();
                FieldInfo field = null;
                Type t = type;
                while (t != null && field == null)
                {
                    field = t.GetField("m_Colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? t.GetField("Colliders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    t = t.BaseType;
                }

                if (field != null)
                {
                    var list = field.GetValue(bone) as IList;
                    if (list == null)
                    {
                        // If null, create a new List<DynamicBoneCollider>
                        var listType = typeof(List<>).MakeGenericType(typeof(DynamicBoneCollider));
                        list = Activator.CreateInstance(listType) as IList;
                        field.SetValue(bone, list);
                    }

                    if (list != null)
                    {
                        foreach (var hc in _handColliders)
                        {
                            if (!list.Contains(hc))
                            {
                                list.Add(hc);
                            }
                        }

                        lock (_registeredBonesReflection)
                        {
                            _registeredBonesReflection.Add(bone);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                VRLog.Error("Reflection error in RegisterCollidersReflection: " + ex.Message);
            }
        }

        private bool HandCollidersStillAttached(MonoBehaviour bone)
        {
            FieldInfo field = FindField(bone.GetType(), "m_Colliders", "Colliders");
            if (field == null) return true;
            IList list = field.GetValue(bone) as IList;
            if (list == null) return false;
            for (int i = 0; i < _handColliders.Count; i++)
            {
                DynamicBoneCollider collider = _handColliders[i];
                if (collider != null && list.Contains(collider))
                    return true;
            }
            return false;
        }

        private sealed class ParticleLayout
        {
            public FieldInfo ListField;
            public FieldInfo Position;
            public FieldInfo Previous;
            public FieldInfo Transform;
            public FieldInfo InitLocalPosition;
            public FieldInfo InitLocalRotation;
        }

        private readonly Dictionary<Type, ParticleLayout> _particleLayouts = new Dictionary<Type, ParticleLayout>();
        private readonly List<MonoBehaviour> _recoverScratch = new List<MonoBehaviour>(64);
        private readonly List<Vector3> _handPoints = new List<Vector3>(16);
        private bool _recoverLogged;
        private float _nextFullRecover;

        internal void RecoverExplodedBones()
        {
            _recoverScratch.Clear();
            lock (_registeredBonesReflection)
            {
                foreach (MonoBehaviour bone in _registeredBonesReflection)
                {
                    if (bone != null)
                        _recoverScratch.Add(bone);
                }
            }
            _handPoints.Clear();
            for (int i = 0; i < _handColliders.Count; i++)
            {
                DynamicBoneCollider collider = _handColliders[i];
                if (collider == null || !collider.enabled) continue;
                Vector3 point = collider.transform.position;
                if (IsFinite(point))
                    _handPoints.Add(point);
            }
            bool full = Time.unscaledTime >= _nextFullRecover;
            if (full)
                _nextFullRecover = Time.unscaledTime + 0.5f;
            for (int i = 0; i < _recoverScratch.Count; i++)
            {
                MonoBehaviour bone = _recoverScratch[i];
                if (bone == null) continue;
                if (!full && !NearHand(bone.transform.position)) continue;
                try
                {
                    RecoverBone(bone);
                }
                catch (Exception ex)
                {
                    if (!_recoverLogged)
                    {
                        _recoverLogged = true;
                        VRLog.Warn("DynamicBone recovery skipped a chain: " + ex.Message);
                    }
                }
            }
        }

        private bool NearHand(Vector3 position)
        {
            if (!IsFinite(position)) return true;
            for (int i = 0; i < _handPoints.Count; i++)
            {
                if ((position - _handPoints[i]).sqrMagnitude < 4f)
                    return true;
            }
            return false;
        }

        private void RecoverBone(MonoBehaviour bone)
        {
            ParticleLayout layout = GetLayout(bone.GetType());
            if (layout == null || layout.ListField == null || layout.Position == null || layout.Previous == null)
                return;
            IList list = layout.ListField.GetValue(bone) as IList;
            if (list == null) return;
            Vector3 root = bone.transform.position;
            for (int i = 0; i < list.Count; i++)
            {
                object particle = list[i];
                if (particle == null) continue;
                Vector3 pos = (Vector3)layout.Position.GetValue(particle);
                Vector3 prev = (Vector3)layout.Previous.GetValue(particle);
                Transform particleTransform = layout.Transform != null
                    ? layout.Transform.GetValue(particle) as Transform
                    : null;
                bool bad = !IsFinite(pos) || !IsFinite(prev)
                    || (particleTransform != null && !IsFinite(particleTransform.position));
                if (!bad && particleTransform != null && IsFinite(particleTransform.position))
                {
                    float fromTransform = (pos - particleTransform.position).sqrMagnitude;
                    float step = (pos - prev).sqrMagnitude;
                    bad = fromTransform > 2.25f && step > 0.25f;
                }
                if (!bad && IsFinite(root) && IsFinite(pos) && (pos - root).sqrMagnitude > 64f)
                    bad = true;
                if (!bad) continue;

                Vector3 recovered = IsFinite(root) ? root : Vector3.zero;
                if (particleTransform != null)
                {
                    if (!IsFinite(particleTransform.position) || !IsFinite(particleTransform.localPosition))
                        RestoreRestPose(layout, particle, particleTransform);
                    if (IsFinite(particleTransform.position))
                        recovered = particleTransform.position;
                }
                if (!IsFinite(recovered))
                    recovered = Vector3.zero;
                layout.Position.SetValue(particle, recovered);
                layout.Previous.SetValue(particle, recovered);
                if (particleTransform != null)
                    particleTransform.position = recovered;
                if (!_recoverLogged)
                {
                    _recoverLogged = true;
                    VRLog.Warn("Reset a DynamicBone particle that left the character.");
                }
            }
        }

        private static void RestoreRestPose(ParticleLayout layout, object particle, Transform particleTransform)
        {
            Vector3 init = Vector3.zero;
            if (layout.InitLocalPosition != null)
            {
                Vector3 stored = (Vector3)layout.InitLocalPosition.GetValue(particle);
                if (IsFinite(stored)) init = stored;
            }
            particleTransform.localPosition = init;
            if (layout.InitLocalRotation != null)
            {
                Quaternion stored = (Quaternion)layout.InitLocalRotation.GetValue(particle);
                particleTransform.localRotation = IsFinite(stored) ? stored : Quaternion.identity;
            }
            else
            {
                particleTransform.localRotation = Quaternion.identity;
            }
        }

        private ParticleLayout GetLayout(Type boneType)
        {
            ParticleLayout layout;
            if (_particleLayouts.TryGetValue(boneType, out layout))
                return layout;
            layout = new ParticleLayout();
            layout.ListField = FindField(boneType, "m_Particles", "Particles");
            if (layout.ListField != null)
            {
                Type particleType = layout.ListField.FieldType.GetGenericArguments()[0];
                layout.Position = FindField(particleType, "m_Position", "Position");
                layout.Previous = FindField(particleType, "m_PrevPosition", "PrevPosition");
                layout.Transform = FindField(particleType, "m_Transform", "Transform");
                layout.InitLocalPosition = FindField(particleType, "m_InitLocalPosition", "InitLocalPosition");
                layout.InitLocalRotation = FindField(particleType, "m_InitLocalRotation", "InitLocalRotation");
            }
            _particleLayouts[boneType] = layout;
            return layout;
        }

        private static FieldInfo FindField(Type type, string primary, string fallback)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(primary, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? current.GetField(fallback, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field;
                current = current.BaseType;
            }
            return null;
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

    [DefaultExecutionOrder(32000)]
    internal sealed class VRDynamicBoneGuard : MonoBehaviour
    {
        private DynamicBoneColliderManager _manager;

        private void LateUpdate()
        {
            if (_manager == null)
                _manager = GetComponent<DynamicBoneColliderManager>();
            if (_manager != null)
                _manager.RecoverExplodedBones();
        }
    }
}
