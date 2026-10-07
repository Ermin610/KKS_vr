using System;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    public class VRHandHapticTrigger : MonoBehaviour
    {
        private struct TargetInfo
        {
            public bool isValid;
            public bool isBreast;

            public TargetInfo(bool valid, bool breast)
            {
                isValid = valid;
                isBreast = breast;
            }
        }

        // One stream per hand. 0.05s let every fingertip and proxy collider
        // retrigger before the shared stamp landed, which flooded the haptic action.
        private const float PulseCooldown = 0.12f;
        private const float FailureBackoff = 1f;
        private const int MaxCachedColliders = 512;
        private const int LeftHandPulseKey = -1000001;
        private const int RightHandPulseKey = -1000002;
        private static readonly Dictionary<Collider, TargetInfo> TargetCache = new Dictionary<Collider, TargetInfo>();
        private static readonly Dictionary<int, float> LastPulseByDevice = new Dictionary<int, float>();
        private static bool _hapticActionFailed;
        private static bool _targetScanFailed;

        public SteamVR_TrackedObject trackedObject;
        public bool isLeftHand;
        private KKCharaStudioVRSettings _settings;
        private int _pulseKey;

        private void Start()
        {
            if (trackedObject == null)
                trackedObject = GetComponentInParent<SteamVR_TrackedObject>();

            if (trackedObject == null)
            {
                VRGIN.Controls.LeftController left = GetComponentInParent<VRGIN.Controls.LeftController>();
                if (left != null)
                {
                    trackedObject = left.GetComponent<SteamVR_TrackedObject>();
                    isLeftHand = true;
                }
                else
                {
                    VRGIN.Controls.RightController right = GetComponentInParent<VRGIN.Controls.RightController>();
                    if (right != null)
                    {
                        trackedObject = right.GetComponent<SteamVR_TrackedObject>();
                        isLeftHand = false;
                    }
                }
            }
            else
            {
                VRGIN.Controls.LeftController left = GetComponentInParent<VRGIN.Controls.LeftController>()
                    ?? trackedObject.GetComponent<VRGIN.Controls.LeftController>();
                if (left != null) isLeftHand = true;
            }

            ResolveSettings();
        }

        private void ResolveSettings()
        {
            if (VR.Manager != null && VR.Manager.Context != null)
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
        }

        private void OnTriggerStay(Collider other)
        {
            try
            {
                PulseForContact(other);
            }
            catch (Exception ex)
            {
                NoteHapticFailure(ex);
            }
        }

        private void PulseForContact(Collider other)
        {
            if (_settings == null) ResolveSettings();
            if (_settings == null || !_settings.HapticFeedbackEnabled) return;
            if (other == null) return;
            if (trackedObject == null)
                trackedObject = GetComponentInParent<SteamVR_TrackedObject>();

            int deviceIndex = trackedObject != null
                && trackedObject.index != SteamVR_TrackedObject.EIndex.None
                ? (int)trackedObject.index
                : -1;
            int pulseKey = ResolvePulseKey(deviceIndex);
            float now = Time.unscaledTime;
            float lastPulse;
            if (LastPulseByDevice.TryGetValue(pulseKey, out lastPulse) &&
                now - lastPulse < PulseCooldown)
                return;

            TargetInfo target = GetTargetInfo(other);
            if (!target.isValid) return;
            if (_settings.VibrateOnlyOnBreasts && !target.isBreast) return;

            SteamVR_Controller.Device device = ResolveDevice(deviceIndex);
            if (device == null || !device.connected) return;

            // Stamp before the driver call so sibling colliders in this physics
            // step cannot pulse, and a throw cannot retry next FixedUpdate.
            LastPulseByDevice[pulseKey] = now;
            float scaled = _settings.HapticFeedbackIntensity * 2000f;
            if (float.IsNaN(scaled) || float.IsInfinity(scaled))
                scaled = 100f;
            ushort duration = (ushort)Mathf.Clamp(scaled, 100f, 3999f);
            device.TriggerHapticPulse(duration, EVRButtonId.k_EButton_Axis0);

            if (VRHandModelManager.Instance != null)
                VRHandModelManager.Instance.NotifyTouch(isLeftHand);
        }

        private int ResolvePulseKey(int deviceIndex)
        {
            if (_pulseKey != 0)
                return _pulseKey;

            if (GetComponentInParent<VRGIN.Controls.LeftController>() != null)
            {
                isLeftHand = true;
                _pulseKey = LeftHandPulseKey;
                return _pulseKey;
            }
            if (GetComponentInParent<VRGIN.Controls.RightController>() != null)
            {
                isLeftHand = false;
                _pulseKey = RightHandPulseKey;
                return _pulseKey;
            }
            if (deviceIndex >= 0)
            {
                _pulseKey = deviceIndex + 1;
                return _pulseKey;
            }
            // Parent controller may appear after the first contact. Do not cache
            // the instance id or each fingertip keeps its own pulse stream.
            return GetInstanceID();
        }

        private void NoteHapticFailure(Exception ex)
        {
            int pulseKey = _pulseKey != 0 ? _pulseKey : GetInstanceID();
            LastPulseByDevice[pulseKey] = Time.unscaledTime + FailureBackoff;
            if (_hapticActionFailed)
                return;
            _hapticActionFailed = true;
            VRLog.Warn("Hand haptic failed: " + ex.Message);
        }

        private SteamVR_Controller.Device ResolveDevice(int deviceIndex)
        {
            if (deviceIndex >= 0)
            {
                SteamVR_Controller.Device indexed = SteamVR_Controller.Input(deviceIndex);
                if (indexed != null)
                    return indexed;
            }

#if KKS
            VRGIN.Controls.Controller controller = GetComponentInParent<VRGIN.Controls.Controller>();
            return controller != null ? SteamVR_Controller.ForController(controller) : null;
#else
            return null;
#endif
        }

        private static TargetInfo GetTargetInfo(Collider collider)
        {
            try
            {
                return LookupTargetInfo(collider);
            }
            catch (Exception ex)
            {
                if (!_targetScanFailed)
                {
                    _targetScanFailed = true;
                    VRLog.Warn("Haptic target scan failed: " + ex.Message);
                }
                TargetInfo invalid = new TargetInfo(false, false);
                if (collider != null)
                    TargetCache[collider] = invalid;
                return invalid;
            }
        }

        private static TargetInfo LookupTargetInfo(Collider collider)
        {
            TargetInfo cached;
            if (collider != null && TargetCache.TryGetValue(collider, out cached)) return cached;
            if (TargetCache.Count >= MaxCachedColliders)
                PruneTargetCache();

            bool isBreast = false;
            bool hasDynamicBone = false;
            Transform current = collider.transform;
            while (current != null)
            {
                string objectName = current.name ?? string.Empty;
                if (ContainsIgnoreCase(objectName, "vrhand") ||
                    ContainsIgnoreCase(objectName, "controller") ||
                    ContainsIgnoreCase(objectName, "trackedobject") ||
                    ContainsIgnoreCase(objectName, "steamvr") ||
                    (objectName.StartsWith("l_", StringComparison.OrdinalIgnoreCase) && objectName.EndsWith("_col", StringComparison.OrdinalIgnoreCase)) ||
                    (objectName.StartsWith("r_", StringComparison.OrdinalIgnoreCase) && objectName.EndsWith("_col", StringComparison.OrdinalIgnoreCase)))
                {
                    cached = new TargetInfo(false, false);
                    TargetCache[collider] = cached;
                    return cached;
                }

                if (ContainsIgnoreCase(objectName, "mune") ||
                    ContainsIgnoreCase(objectName, "glands") ||
                    ContainsIgnoreCase(objectName, "breast"))
                    isBreast = true;

                if (!hasDynamicBone)
                {
                    MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
                    foreach (MonoBehaviour behaviour in behaviours)
                    {
                        if (behaviour == null) continue;
                        string typeName = behaviour.GetType().Name;
                        if (typeName.IndexOf("DynamicBone", StringComparison.Ordinal) >= 0 &&
                            typeName.IndexOf("Collider", StringComparison.Ordinal) < 0)
                        {
                            hasDynamicBone = true;
                            break;
                        }
                    }
                }
                current = current.parent;
            }

            bool hasCharacterMesh = false;
            SkinnedMeshRenderer skinnedMesh = collider.GetComponentInParent<SkinnedMeshRenderer>();
            if (skinnedMesh != null)
            {
                string rendererName = skinnedMesh.name ?? string.Empty;
                hasCharacterMesh = !ContainsIgnoreCase(rendererName, "o_hand") &&
                    !ContainsIgnoreCase(rendererName, "silhouette") &&
                    !ContainsIgnoreCase(rendererName, "vrhand");
            }

            cached = new TargetInfo(hasCharacterMesh || hasDynamicBone, isBreast);
            TargetCache[collider] = cached;
            return cached;
        }

        private static void PruneTargetCache()
        {
            List<Collider> dead = null;
            foreach (Collider key in TargetCache.Keys)
            {
                if (key != null)
                    continue;
                if (dead == null)
                    dead = new List<Collider>();
                dead.Add(key);
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    TargetCache.Remove(dead[i]);
            }
            if (TargetCache.Count >= MaxCachedColliders)
                TargetCache.Clear();
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
