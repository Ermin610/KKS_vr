using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// Velocity-Sensitive Slap & Petting System (速度感应拍打与抚摸系统) for KKS Studio VR.
    /// Tracks 6-DOF linear velocity and angular velocity of VR hand controllers and contacts in real time.
    /// Features Dual-Modal Response:
    /// 1. 抚摸模式 (Caress/Petting Mode): Speed < 0.6 m/s or palm/fingertip surface sliding:
    ///    - Smooth, fine haptic micro-pulses (180~280 us high-frequency tick);
    ///    - Soft friction sound effects (VRSFXService.Sfx.Traverse / rub_soft_*.wav);
    ///    - Character micro-expressions (blushing, eyelid squinting, gentle blinking, shy gaze diversion).
    /// 2. 拍打模式 (Slap/Boop Mode): Speed >= 0.8 m/s:
    ///    - Heavy haptic kick (3200~3999 us pulse + 32ms secondary elastic rebound echo);
    ///    - Resonant slap impact sound effects (VRSFXService.Sfx.Slap / slap_impact_*.wav);
    ///    - DynamicBone instantaneous impulse force and damped harmonic ripple wave (VRDynamicBoneImpulse);
    ///    - FinalIK body recoil displacement (VRTouchReaction);
    ///    - Startled facial reaction (VRPettingExpressionController).
    /// 3. 中速轻拍模式 (Firm Tap Mode): 0.6 m/s <= Speed < 0.8 m/s:
    ///    - Intermediate tactile tap and Tap sound effects.
    /// </summary>
    public class VRPhysicalTouchHandler : MonoBehaviour
    {
        public enum BodyPartType
        {
            None,
            Head,
            MuneL,
            MuneR,
            UpperBody,
            LowerBody,
            Groin,      // Buttocks / Hips
            Asoko,      // Crotch / Genitals (Wet)
            ArmL,
            ArmR,
            ForearmL,
            ForearmR,
            HandL,
            HandR,
            ThighL,
            ThighR,
            LegL,
            LegR,
            FootL,
            FootR
        }

        // Velocity thresholds
        public const float PettingSpeedMax = 0.60f;     // Speeds below 0.6 m/s trigger Caress/Petting
        public const float SlapSpeedMin = 0.80f;        // Speeds >= 0.8 m/s trigger Slap/Boop
        public const float TraverseMinSpeed = 0.05f;    // Minimum speed to trigger sliding friction SFX
        private const float TraverseInterval = 0.18f;   // Interval between friction micro-vibrations and audio ticks
        private const float SlapCooldown = 0.15f;       // Cooldown between consecutive slap impacts
        private const float HapticCooldown = 0.035f;

        public SteamVR_TrackedObject trackedObject;
        public bool isLeftHand;

        // OnTriggerStay 每帧都会进来；Collider -> (角色, 部位) 的解析涉及 GetComponentInParent、
        // ToLowerInvariant 与数十次 Contains，缓存后可以避免持续触摸时的逐帧 GC 与字符串开销。
        private struct ContactInfo
        {
            public ChaControl Chara;
            public BodyPartType Part;
            public string ColliderName;
        }

        private const int ContactCacheLimit = 512;
        private static readonly Dictionary<int, ContactInfo> ContactCache = new Dictionary<int, ContactInfo>();
        private static readonly HashSet<int> NonCharacterColliders = new HashSet<int>();

        private VRVelocityTracker _velocityTracker;
        private float _lastSlapTime;
        private float _lastTraverseTime;
        private float _lastPulseTime;
        private KKCharaStudioVRSettings _settings;

        public VRVelocityTracker VelocityTracker => _velocityTracker;
        public Vector3 LinearVelocity => _velocityTracker != null ? _velocityTracker.LinearVelocity : Vector3.zero;
        public Vector3 AngularVelocity => _velocityTracker != null ? _velocityTracker.AngularVelocity : Vector3.zero;

        private void Awake()
        {
            EnsureVelocityTracker();
        }

        private void Start()
        {
            ResolveTrackedObject();
            EnsureVelocityTracker();
            ResolveSettings();
        }

        private void ResolveTrackedObject()
        {
            if (trackedObject == null)
            {
                trackedObject = GetComponentInParent<SteamVR_TrackedObject>() ?? GetComponent<SteamVR_TrackedObject>();
            }

            if (trackedObject == null)
            {
                var left = GetComponentInParent<VRGIN.Controls.LeftController>();
                if (left != null)
                {
                    trackedObject = left.GetComponent<SteamVR_TrackedObject>();
                    isLeftHand = true;
                }
                else
                {
                    var right = GetComponentInParent<VRGIN.Controls.RightController>();
                    if (right != null)
                    {
                        trackedObject = right.GetComponent<SteamVR_TrackedObject>();
                        isLeftHand = false;
                    }
                }
            }
            else
            {
                var left = GetComponentInParent<VRGIN.Controls.LeftController>()
                    ?? trackedObject.GetComponent<VRGIN.Controls.LeftController>();
                if (left != null) isLeftHand = true;
            }
        }

        private void EnsureVelocityTracker()
        {
            if (_velocityTracker == null)
            {
                _velocityTracker = GetComponent<VRVelocityTracker>();
                if (_velocityTracker == null)
                {
                    _velocityTracker = gameObject.AddComponent<VRVelocityTracker>();
                }
            }
            if (_velocityTracker.trackedObject == null && trackedObject != null)
            {
                _velocityTracker.trackedObject = trackedObject;
            }
        }

        private void ResolveSettings()
        {
            if (VR.Manager != null && VR.Manager.Context != null)
            {
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
            }
        }

        private SteamVR_Controller.Device ResolveDevice()
        {
            int deviceIndex = trackedObject != null && trackedObject.index != SteamVR_TrackedObject.EIndex.None
                ? (int)trackedObject.index
                : -1;

            if (deviceIndex >= 0)
            {
                var indexed = SteamVR_Controller.Input(deviceIndex);
                if (indexed != null) return indexed;
            }

            var controller = GetComponentInParent<VRGIN.Controls.Controller>();
            return controller != null ? SteamVR_Controller.ForController(controller) : null;
        }

        private void OnTriggerEnter(Collider other)
        {
            Vector3 contactPos = other != null ? other.ClosestPoint(transform.position) : transform.position;
            ProcessContact(other, isInitialContact: true, contactPos);
        }

        private void OnTriggerStay(Collider other)
        {
            Vector3 contactPos = other != null ? other.ClosestPoint(transform.position) : transform.position;
            ProcessContact(other, isInitialContact: false, contactPos);
        }

        public void ProcessContact(Collider other, bool isInitialContact)
        {
            Vector3 contactPos = other != null ? other.ClosestPoint(transform.position) : transform.position;
            ProcessContact(other, isInitialContact, contactPos);
        }

        public void ProcessContact(Collider other, bool isInitialContact, Vector3 contactPos)
        {
            if (other == null) return;
            if (!VRStudioInteractionPolicy.KneadTouchAllowed(
                    VRInteractionOptions.DynamicTouchEnabled,
                    VRInteractionOptions.PhysicalUndressEnabled))
                return;
            if (VRHandCollision.ShouldIgnore(other)) return;

            ContactInfo info;
            int colliderId = other.GetInstanceID();
            // 角色被删除后缓存里的 ChaControl 会变成 Unity 空对象，此时需要重新解析
            if (!ContactCache.TryGetValue(colliderId, out info) || info.Chara == null)
            {
                // Floors and props stay in contact every physics step; remember
                // that they are not part of a character instead of re-walking parents.
                if (NonCharacterColliders.Contains(colliderId))
                    return;
                ChaControl resolved = other.GetComponentInParent<ChaControl>();
                if (resolved == null)
                {
                    var smr = other.GetComponentInParent<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        resolved = smr.GetComponentInParent<ChaControl>();
                    }
                }
                if (resolved == null)
                {
                    if (NonCharacterColliders.Count >= ContactCacheLimit)
                        NonCharacterColliders.Clear();
                    NonCharacterColliders.Add(colliderId);
                    return;
                }

                if (ContactCache.Count >= ContactCacheLimit)
                    ContactCache.Clear();
                info = new ContactInfo
                {
                    Chara = resolved,
                    Part = ResolveBodyPart(other.name, other.transform),
                    ColliderName = other.name
                };
                ContactCache[colliderId] = info;
            }
            ChaControl chara = info.Chara;
            if (!chara.gameObject.activeInHierarchy) return;

            if (_velocityTracker == null) EnsureVelocityTracker();
            Vector3 contactVelocity = _velocityTracker != null ? _velocityTracker.GetPointVelocity(contactPos) : Vector3.zero;
            float speed = contactVelocity.magnitude;

            BodyPartType part = info.Part;
            VRSFXService.Surface surface = GetSurface(chara, part, info.ColliderName);
            VRSFXService.Intensity intensity = GetIntensity(part);

            Vector3 pushDirection = contactVelocity.sqrMagnitude > 0.01f
                ? contactVelocity.normalized
                : (contactPos - transform.position).normalized;

            if (pushDirection.sqrMagnitude < 0.001f)
            {
                pushDirection = transform.forward;
            }

            // =========================================================================
            // Mode 1: 拍打模式 (SLAP / BOOP MODE) — High Speed (speed >= 0.8 m/s)
            // =========================================================================
            if (speed >= SlapSpeedMin)
            {
                if (isInitialContact || Time.unscaledTime - _lastSlapTime > SlapCooldown)
                {
                    _lastSlapTime = Time.unscaledTime;
                    _lastPulseTime = Time.unscaledTime;

                    // 1. Play crisp & loud Slap SFX
                    float volume = Mathf.Clamp(0.85f + speed * 0.35f, 0.85f, 1.6f);
                    VRSFXService.Instance.Play(VRSFXService.Sfx.Slap, surface, intensity, contactPos, volume);

                    // 2. Heavy tactile haptic kick + secondary elastic rebound echo
                    ushort kickDuration = (ushort)Mathf.Clamp(3000f + speed * 500f, 2600f, 3999f);
                    TriggerHaptic(kickDuration);
                    StartCoroutine(Co_HapticReboundEcho(intensity: 1100));

                    // 3. Inject instantaneous DynamicBone velocity kick & ripple oscillation
                    VRDynamicBoneImpulse.Instance.ApplySlapImpulse(chara, part, contactVelocity, contactPos, speed);

                    // 4. Inject recoil displacement into character FinalIK effectors
                    var reaction = VRTouchReaction.GetOrAttach(chara);
                    if (reaction != null)
                    {
                        int touchId = ConvertToTouchReactionId(part);
                        reaction.React(touchId, pushDirection, speed);
                    }

                    // 5. Trigger character startled micro-expression
                    var expr = VRPettingExpressionController.GetOrAttach(chara);
                    if (expr != null)
                    {
                        expr.OnSlap(part, speed);
                    }

                    if (VRHandModelManager.Instance != null)
                    {
                        VRHandModelManager.Instance.NotifyTouch(isLeftHand);
                    }
                }
            }
            // =========================================================================
            // Mode 2: 抚摸模式 (CARESS / PETTING MODE) — Low Speed (speed < 0.6 m/s)
            // =========================================================================
            else if (speed < PettingSpeedMax)
            {
                // Trigger character petting micro-expressions (blushing, eyelid squinting, shy gaze)
                var expr = VRPettingExpressionController.GetOrAttach(chara);
                if (expr != null)
                {
                    expr.OnPettingStay(part, speed);
                }

                if (isInitialContact)
                {
                    // Initial soft contact
                    if (Time.unscaledTime - _lastPulseTime > HapticCooldown)
                    {
                        _lastPulseTime = Time.unscaledTime;

                        float volume = Mathf.Clamp(0.35f + speed * 0.4f, 0.30f, 0.65f);
                        VRSFXService.Instance.Play(VRSFXService.Sfx.Tap, surface, intensity, contactPos, volume);

                        // Gentle initial touch micro-pulse
                        TriggerHaptic(220);

                        if (VRHandModelManager.Instance != null)
                        {
                            VRHandModelManager.Instance.NotifyTouch(isLeftHand);
                        }
                    }
                }
                else if (speed >= TraverseMinSpeed)
                {
                    // Continuous surface sliding / rubbing friction
                    if (Time.unscaledTime - _lastTraverseTime >= TraverseInterval)
                    {
                        _lastTraverseTime = Time.unscaledTime;
                        _lastPulseTime = Time.unscaledTime;

                        float volume = Mathf.Clamp(0.30f + speed * 0.5f, 0.28f, 0.60f);
                        VRSFXService.Instance.Play(VRSFXService.Sfx.Traverse, surface, intensity, contactPos, volume);

                        // Smooth, fine haptic micro-pulse train
                        ushort microPulse = (ushort)Mathf.Clamp(180f + speed * 250f, 150f, 320f);
                        TriggerHaptic(microPulse);

                        if (VRHandModelManager.Instance != null)
                        {
                            VRHandModelManager.Instance.NotifyTouch(isLeftHand);
                        }
                    }
                }
            }
            // =========================================================================
            // Mode 3: 中速轻拍 (FIRM TAP DEADBAND) — (0.6 m/s <= speed < 0.8 m/s)
            // =========================================================================
            else
            {
                if (isInitialContact && Time.unscaledTime - _lastPulseTime > HapticCooldown)
                {
                    _lastPulseTime = Time.unscaledTime;

                    float volume = Mathf.Clamp(0.55f + speed * 0.35f, 0.5f, 0.9f);
                    VRSFXService.Instance.Play(VRSFXService.Sfx.Tap, surface, intensity, contactPos, volume);

                    TriggerHaptic(650);

                    var reaction = VRTouchReaction.GetOrAttach(chara);
                    if (reaction != null)
                    {
                        int touchId = ConvertToTouchReactionId(part);
                        reaction.React(touchId, pushDirection, 0.55f);
                    }

                    if (VRHandModelManager.Instance != null)
                    {
                        VRHandModelManager.Instance.NotifyTouch(isLeftHand);
                    }
                }
            }
        }

        public void ProcessContactExit(Collider other)
        {
            // Contact ended; expression controller will automatically manage recovery after timeout
        }

        private IEnumerator Co_HapticReboundEcho(ushort intensity)
        {
            yield return new WaitForSeconds(0.032f);
            TriggerHaptic(intensity);
        }

        private void TriggerHaptic(ushort microSeconds)
        {
            if (_settings == null) ResolveSettings();
            if (_settings != null && !_settings.HapticFeedbackEnabled) return;

            var device = ResolveDevice();
            if (device == null || !device.connected) return;

            float intensity = _settings != null ? _settings.HapticFeedbackIntensity : 0.5f;
            ushort scaled = (ushort)Mathf.Clamp(microSeconds * (intensity * 2f), 100f, 3999f);
            device.TriggerHapticPulse(scaled, EVRButtonId.k_EButton_Axis0);
        }

        public static BodyPartType ResolveBodyPart(string colliderName, Transform trans)
        {
            string name = (colliderName ?? string.Empty).ToLowerInvariant();

            if (name.Contains("kokan") || name.Contains("asoko") || name.Contains("ana") ||
                name.Contains("clit") || name.Contains("pussy") || name.Contains("penis"))
                return BodyPartType.Asoko;

            if (name.Contains("bust02_l") || name.Contains("bust_l") || name.Contains("munel"))
                return BodyPartType.MuneL;
            if (name.Contains("bust02_r") || name.Contains("bust_r") || name.Contains("muner"))
                return BodyPartType.MuneR;
            if (name.Contains("bust") || name.Contains("mune") || name.Contains("breast"))
                return BodyPartType.MuneL;

            if (name.Contains("siri") || name.Contains("waist02") || name.Contains("hip") || name.Contains("butt"))
                return BodyPartType.Groin;

            if (name.Contains("berry") || name.Contains("waist_l") || name.Contains("waist_r") || name.Contains("stomach") || name.Contains("belly"))
                return BodyPartType.LowerBody;

            if (name.Contains("spine") || name.Contains("chest") || name.Contains("neck") || name.Contains("back"))
                return BodyPartType.UpperBody;

            if (name.Contains("head") || name.Contains("cheek") || name.Contains("mouth") || name.Contains("hair") || name.Contains("face"))
                return BodyPartType.Head;

            if (name.Contains("thigh01_l") || name.Contains("thighl") || name.Contains("thigh_l"))
                return BodyPartType.ThighL;
            if (name.Contains("thigh01_r") || name.Contains("thighr") || name.Contains("thigh_r"))
                return BodyPartType.ThighR;
            if (name.Contains("thigh"))
                return BodyPartType.ThighL;

            if (name.Contains("leg02_l") || name.Contains("footl") || name.Contains("foot_l"))
                return BodyPartType.FootL;
            if (name.Contains("leg02_r") || name.Contains("footr") || name.Contains("foot_r"))
                return BodyPartType.FootR;

            if (name.Contains("leg01_l") || name.Contains("legl") || name.Contains("leg_l"))
                return BodyPartType.LegL;
            if (name.Contains("leg01_r") || name.Contains("legr") || name.Contains("leg_r"))
                return BodyPartType.LegR;

            if (name.Contains("arm_l") || name.Contains("shoulder_l"))
                return BodyPartType.ArmL;
            if (name.Contains("arm_r") || name.Contains("shoulder_r"))
                return BodyPartType.ArmR;

            if (name.Contains("wrist_l") || name.Contains("forearm_l"))
                return BodyPartType.ForearmL;
            if (name.Contains("wrist_r") || name.Contains("forearm_r"))
                return BodyPartType.ForearmR;

            if (name.Contains("hand_l")) return BodyPartType.HandL;
            if (name.Contains("hand_r")) return BodyPartType.HandR;

            // Search parent names if specific part wasn't identified
            Transform cur = trans != null ? trans.parent : null;
            while (cur != null)
            {
                string pName = (cur.name ?? string.Empty).ToLowerInvariant();
                if (pName.Contains("mune") || pName.Contains("bust")) return BodyPartType.MuneL;
                if (pName.Contains("head") || pName.Contains("hair")) return BodyPartType.Head;
                if (pName.Contains("spine") || pName.Contains("chest")) return BodyPartType.UpperBody;
                if (pName.Contains("thigh")) return BodyPartType.ThighL;
                cur = cur.parent;
            }

            return BodyPartType.UpperBody;
        }

        public static VRSFXService.Surface GetSurface(ChaControl chara, BodyPartType part, string colliderName)
        {
            if (part == BodyPartType.Head || (colliderName != null && colliderName.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return VRSFXService.Surface.Hair;
            }

            return IsBodyPartClothed(chara, part)
                ? VRSFXService.Surface.Cloth
                : VRSFXService.Surface.Skin;
        }

        public static VRSFXService.Intensity GetIntensity(BodyPartType part)
        {
            switch (part)
            {
                case BodyPartType.Asoko:
                    return VRSFXService.Intensity.Wet;
                case BodyPartType.Groin:
                case BodyPartType.LowerBody:
                    return VRSFXService.Intensity.Hollow;
                case BodyPartType.MuneL:
                case BodyPartType.MuneR:
                case BodyPartType.ThighL:
                case BodyPartType.ThighR:
                    return VRSFXService.Intensity.Soft;
                default:
                    return VRSFXService.Intensity.Hard;
            }
        }

        public static bool IsBodyPartClothed(ChaControl chara, BodyPartType part)
        {
            if (chara == null || chara.fileStatus == null || chara.fileStatus.clothesState == null)
                return false;

            int[] slots = GetClothingSlots(part);
            if (slots == null || slots.Length == 0)
                return false;

            for (int i = 0; i < slots.Length; i++)
            {
                int slot = slots[i];
                if (slot >= 0 && slot < chara.fileStatus.clothesState.Length)
                {
                    bool isClothes;
                    try
                    {
                        isClothes = chara.IsClothes(slot);
                    }
                    catch
                    {
                        // 换装/重载期间衣物对象可能暂时为空
                        isClothes = false;
                    }
                    if (isClothes && chara.fileStatus.clothesState[slot] == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static readonly int[] SlotsMune = { 0, 2 };        // Top, Bra
        private static readonly int[] SlotsUpper = { 0 };          // Top
        private static readonly int[] SlotsLower = { 0, 1 };       // Top, Bottom
        private static readonly int[] SlotsGroin = { 1, 3, 5 };    // Bottom, Underwear, Pantyhose
        private static readonly int[] SlotsThigh = { 1, 5, 6 };    // Bottom, Pantyhose, Socks
        private static readonly int[] SlotsArm = { 0, 4 };         // Top, Gloves
        private static readonly int[] SlotsLeg = { 5, 6, 7 };      // Pantyhose, Socks, Shoes

        private static int[] GetClothingSlots(BodyPartType part)
        {
            switch (part)
            {
                case BodyPartType.MuneL:
                case BodyPartType.MuneR:
                    return SlotsMune;
                case BodyPartType.UpperBody:
                    return SlotsUpper;
                case BodyPartType.LowerBody:
                    return SlotsLower;
                case BodyPartType.Groin:
                case BodyPartType.Asoko:
                    return SlotsGroin;
                case BodyPartType.ThighL:
                case BodyPartType.ThighR:
                    return SlotsThigh;
                case BodyPartType.ArmL:
                case BodyPartType.ArmR:
                case BodyPartType.ForearmL:
                case BodyPartType.ForearmR:
                    return SlotsArm;
                case BodyPartType.LegL:
                case BodyPartType.LegR:
                case BodyPartType.FootL:
                case BodyPartType.FootR:
                    return SlotsLeg;
                default:
                    return null;
            }
        }

        public static int ConvertToTouchReactionId(BodyPartType part)
        {
            switch (part)
            {
                case BodyPartType.LowerBody: return 0;
                case BodyPartType.MuneL:
                case BodyPartType.ArmL: return 1;
                case BodyPartType.MuneR:
                case BodyPartType.ArmR: return 2;
                case BodyPartType.ThighL: return 3;
                case BodyPartType.ThighR: return 4;
                case BodyPartType.HandL:
                case BodyPartType.ForearmL: return 5;
                case BodyPartType.HandR:
                case BodyPartType.ForearmR: return 6;
                case BodyPartType.FootL: return 7;
                case BodyPartType.FootR: return 8;
                case BodyPartType.Head:
                case BodyPartType.UpperBody: return 9;
                case BodyPartType.Asoko:
                case BodyPartType.Groin: return 10;
                case BodyPartType.LegL: return 11;
                case BodyPartType.LegR: return 12;
                default: return 0;
            }
        }
    }
}
