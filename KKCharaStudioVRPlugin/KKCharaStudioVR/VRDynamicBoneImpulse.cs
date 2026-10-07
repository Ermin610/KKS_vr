using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// Physical impulse and elastic ripple injection system for character DynamicBones in KKS Studio VR.
    /// When a slap (Slap/Boop) hits a soft body part (breasts, buttocks, thighs):
    /// 1. Instantaneous Verlet particle velocity kick:
    ///    Directly displaces particle prevPosition backwards (p_prev = p - v * dt * scale),
    ///    triggering an instant kinetic jolt on the very next simulation frame without mesh explosion.
    /// 2. Damped Harmonic Oscillation Wave (物理晃动波纹协程):
    ///    Modulates DynamicBone.m_Force over ~0.35s using F(t) = F_peak * e^(-lambda*t) * sin(2*pi*f*t),
    ///    producing realistic fluid elastic ripple and flesh rebound.
    /// 3. Sympathetic resonance (对侧共振): Hard slap on one breast transmits 35% ripple wave to the other.
    /// </summary>
    public class VRDynamicBoneImpulse : MonoBehaviour
    {
        private static VRDynamicBoneImpulse _instance;

        public static VRDynamicBoneImpulse Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("VRDynamicBoneImpulse");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<VRDynamicBoneImpulse>();
                }
                return _instance;
            }
        }

        // Active ripple coroutines mapped to DynamicBone instances to prevent stacking overflows
        private readonly Dictionary<MonoBehaviour, Coroutine> _activeRipples = new Dictionary<MonoBehaviour, Coroutine>();

        /// <summary>
        /// Injects velocity-sensitive slap impulse and ripple into the character's DynamicBones.
        /// </summary>
        public void ApplySlapImpulse(ChaControl chara, VRPhysicalTouchHandler.BodyPartType part, Vector3 velocity, Vector3 contactPoint, float speed)
        {
            if (chara == null) return;

            float speedFactor = Mathf.Clamp(speed, 0.8f, 3.0f);
            Vector3 impactDirection = velocity.sqrMagnitude > 0.001f ? velocity.normalized : Vector3.down;

            switch (part)
            {
                case VRPhysicalTouchHandler.BodyPartType.MuneL:
                    InjectBreastImpulse(chara, isLeft: true, impactDirection, speedFactor);
                    InjectBreastSympathetic(chara, isLeft: false, impactDirection, speedFactor * 0.35f);
                    break;

                case VRPhysicalTouchHandler.BodyPartType.MuneR:
                    InjectBreastImpulse(chara, isLeft: false, impactDirection, speedFactor);
                    InjectBreastSympathetic(chara, isLeft: true, impactDirection, speedFactor * 0.35f);
                    break;

                case VRPhysicalTouchHandler.BodyPartType.Groin: // Buttocks
                    InjectButtockImpulse(chara, impactDirection, speedFactor, contactPoint);
                    break;

                case VRPhysicalTouchHandler.BodyPartType.ThighL:
                case VRPhysicalTouchHandler.BodyPartType.ThighR:
                    InjectThighImpulse(chara, part == VRPhysicalTouchHandler.BodyPartType.ThighL, impactDirection, speedFactor);
                    break;
            }
        }

        private void InjectBreastImpulse(ChaControl chara, bool isLeft, Vector3 impactDir, float speedFactor)
        {
            List<MonoBehaviour> bones = FindBones(chara, isLeft ? "bust01_l" : "bust01_r", isLeft ? "bust_l" : "bust_r", isLeft ? "mune_l" : "mune_r");
            float impulseScale = 0.035f * speedFactor;
            Vector3 forcePeak = impactDir * (0.85f * speedFactor);

            for (int i = 0; i < bones.Count; i++)
            {
                KickParticles(bones[i], impactDir * impulseScale);
                StartRippleWave(bones[i], forcePeak, frequency: 5.8f, damping: 8.5f, duration: 0.36f);
            }
        }

        private void InjectBreastSympathetic(ChaControl chara, bool isLeft, Vector3 impactDir, float speedFactor)
        {
            List<MonoBehaviour> bones = FindBones(chara, isLeft ? "bust01_l" : "bust01_r", isLeft ? "bust_l" : "bust_r", isLeft ? "mune_l" : "mune_r");
            Vector3 forcePeak = impactDir * (0.45f * speedFactor);

            for (int i = 0; i < bones.Count; i++)
            {
                StartRippleWave(bones[i], forcePeak, frequency: 5.5f, damping: 9.0f, duration: 0.28f);
            }
        }

        private void InjectButtockImpulse(ChaControl chara, Vector3 impactDir, float speedFactor, Vector3 contactPoint)
        {
            // Determine if left or right buttock was hit based on character local coordinates
            bool isLeft = true;
            if (chara.objBodyBone != null)
            {
                Vector3 localHit = chara.objBodyBone.transform.InverseTransformPoint(contactPoint);
                isLeft = localHit.x >= 0f;
            }

            List<MonoBehaviour> primaryBones = FindBones(chara, isLeft ? "siri_l" : "siri_r", isLeft ? "siri01_l" : "siri01_r");
            List<MonoBehaviour> secondaryBones = FindBones(chara, isLeft ? "siri_r" : "siri_l", isLeft ? "siri01_r" : "siri01_l");

            float impulseScale = 0.032f * speedFactor;
            Vector3 forcePeak = impactDir * (0.80f * speedFactor);

            for (int i = 0; i < primaryBones.Count; i++)
            {
                KickParticles(primaryBones[i], impactDir * impulseScale);
                StartRippleWave(primaryBones[i], forcePeak, frequency: 6.2f, damping: 9.0f, duration: 0.32f);
            }

            for (int i = 0; i < secondaryBones.Count; i++)
            {
                StartRippleWave(secondaryBones[i], forcePeak * 0.35f, frequency: 6.0f, damping: 9.5f, duration: 0.25f);
            }
        }

        private void InjectThighImpulse(ChaControl chara, bool isLeft, Vector3 impactDir, float speedFactor)
        {
            List<MonoBehaviour> bones = FindBones(chara, isLeft ? "thigh00_l" : "thigh00_r", isLeft ? "thigh_l" : "thigh_r");
            float impulseScale = 0.020f * speedFactor;
            Vector3 forcePeak = impactDir * (0.50f * speedFactor);

            for (int i = 0; i < bones.Count; i++)
            {
                KickParticles(bones[i], impactDir * impulseScale);
                StartRippleWave(bones[i], forcePeak, frequency: 7.0f, damping: 11.0f, duration: 0.22f);
            }
        }

        /// <summary>
        /// Instantly shifts particle m_PrevPosition backwards along the impulse vector,
        /// causing Verlet integration to immediately accelerate the bone on the next frame.
        /// </summary>
        private void KickParticles(MonoBehaviour bone, Vector3 impulseDisplacement)
        {
            if (bone == null) return;

            // Clamp impulse displacement to prevent mesh explosion
            float maxDisplacement = 0.06f;
            if (impulseDisplacement.sqrMagnitude > maxDisplacement * maxDisplacement)
            {
                impulseDisplacement = impulseDisplacement.normalized * maxDisplacement;
            }

            try
            {
                FieldInfo listField = bone.GetType().GetField("m_Particles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                   ?? bone.GetType().GetField("Particles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (listField != null)
                {
                    System.Collections.IList list = listField.GetValue(bone) as System.Collections.IList;
                    if (list != null)
                    {
                        FieldInfo prevField = null;
                        for (int i = 0; i < list.Count; i++)
                        {
                            object p = list[i];
                            if (p == null) continue;
                            if (prevField == null)
                            {
                                prevField = p.GetType().GetField("m_PrevPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                         ?? p.GetType().GetField("PrevPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            }
                            if (prevField != null)
                            {
                                Vector3 prev = (Vector3)prevField.GetValue(p);
                                prevField.SetValue(p, prev - impulseDisplacement);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Starts a damped sinusoidal harmonic force oscillation coroutine in bone.m_Force.
        /// </summary>
        private void StartRippleWave(MonoBehaviour bone, Vector3 peakForce, float frequency, float damping, float duration)
        {
            if (bone == null) return;

            if (_activeRipples.TryGetValue(bone, out Coroutine existing) && existing != null)
            {
                StopCoroutine(existing);
                _activeRipples.Remove(bone);
            }

            Coroutine co = StartCoroutine(Co_ImpulseRipple(bone, peakForce, frequency, damping, duration));
            _activeRipples[bone] = co;
        }

        private IEnumerator Co_ImpulseRipple(MonoBehaviour bone, Vector3 peakForce, float frequency, float damping, float duration)
        {
            if (bone == null) yield break;

            Vector3 baseForce = GetBoneForce(bone);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (bone == null) yield break;

                elapsed += Time.deltaTime;
                float decay = Mathf.Exp(-damping * elapsed);
                float wave = Mathf.Sin(2f * Mathf.PI * frequency * elapsed);
                Vector3 currentRipple = peakForce * (decay * wave);

                SetBoneForce(bone, baseForce + currentRipple);
                yield return null;
            }

            if (bone != null)
            {
                SetBoneForce(bone, baseForce);
                _activeRipples.Remove(bone);
            }
        }

        private static Vector3 GetBoneForce(MonoBehaviour bone)
        {
            if (bone is DynamicBone db) return db.m_Force;
            if (bone is DynamicBone_Ver01 db1) return db1.m_Force;
            if (bone is DynamicBone_Ver02 db2) return db2.Force;
            return Vector3.zero;
        }

        private static void SetBoneForce(MonoBehaviour bone, Vector3 force)
        {
            if (bone is DynamicBone db) db.m_Force = force;
            else if (bone is DynamicBone_Ver01 db1) db1.m_Force = force;
            else if (bone is DynamicBone_Ver02 db2) db2.Force = force;
        }

        private static List<MonoBehaviour> FindBones(ChaControl chara, params string[] boneKeywords)
        {
            List<MonoBehaviour> result = new List<MonoBehaviour>();
            if (chara == null) return result;

            var behaviours = chara.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;
                string typeName = b.GetType().Name;
                if (!typeName.Contains("DynamicBone") || typeName.Contains("Collider")) continue;

                Transform rootTransform = GetBoneRoot(b);
                string bName = rootTransform != null ? rootTransform.name.ToLowerInvariant() : b.name.ToLowerInvariant();

                for (int k = 0; k < boneKeywords.Length; k++)
                {
                    string keyword = boneKeywords[k].ToLowerInvariant();
                    if (bName.Contains(keyword))
                    {
                        result.Add(b);
                        break;
                    }
                }
            }
            return result;
        }

        private static Transform GetBoneRoot(MonoBehaviour bone)
        {
            if (bone is DynamicBone db) return db.m_Root ?? db.transform;
            if (bone is DynamicBone_Ver01 db1) return db1.m_Root ?? db1.transform;
            if (bone is DynamicBone_Ver02 db2) return db2.Root ?? db2.transform;
            return bone.transform;
        }
    }
}
