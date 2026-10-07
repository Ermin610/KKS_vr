using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Studio;
using UnityEngine;
using UnityEngine.Networking;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// 工作室【物理徒手撕衣服 / 脱衣系统】(Physical Hand Undresser)
    /// 玩家可通过 VR 手柄的 Grip(握把) 抓取并向外/向下拉扯，或按住 Trigger(扳机) 快速划过/撕扯角色的常规服装敏感区域
    /// （上衣、下衣、胸罩、内裤、手套、丝袜、鞋子等），实现极富沉浸感的徒手物理剥离与撕扯交互。
    /// 【安全策略】：绝对严禁触碰任何饰品插槽（Accessory），绝不干涉头发、呆毛、发饰、兽耳等饰品部件，誓死保护角色发型完整！
    /// 包含：
    /// 1. 实时手部拉伸位移量与运动学速度矢量计算；
    /// 2. 双模撕衣机制：拉伸位移撕裂 (Pull Rip) 与 高速划拉撕裂 (Fast Swipe Rip)；
    /// 3. 符合真实物理逻辑的衣物层次推进逻辑（完整 -> 半脱/破损 -> 全脱；外衣未褪时优先褪外衣）；
    /// 4. 动态布料张力阻尼震颤与三段式撕裂脉冲触觉反馈 (Haptic pulses)；
    /// 5. 3D 空间立体声音效联动（支持 strip_fabric_*.wav 及 VRSFXService 音效库，区分硬质/轻薄布料）；
    /// 6. 工作室环境深度适配：近身判定、IK/物件拖拽避让、MMD 兼容性与工作室 UI 即时同步。
    /// </summary>
    public sealed class VRPhysicalUndresser : MonoBehaviour
    {
        public enum UndressPart
        {
            None = 0,
            Top = 1,        // 上衣 / 胸部 / 外套
            Bra = 2,        // 胸罩 / 内衣上装
            Bottom = 3,     // 下装 / 短裙 / 裤子
            Panties = 4,    // 内裤 / 裆部 / 臀部
            Socks = 5,      // 丝袜 / 裤袜 / 袜子
            Gloves = 6,     // 手套 / 手腕 / 袖套
            Shoes = 7       // 室内鞋 / 室外鞋
        }

        private sealed class HandState
        {
            public bool IsLeft;
            public bool IsGrabbing;
            public bool HasTriggered;
            public Vector3 GrabStartHandPos;
            public Vector3 GrabTargetAnchorPos;
            public ChaControl TargetChar;
            public OCIChar TargetOci;
            public UndressPart TargetPart;
            public float LastTensionHapticTime;

            // 速度与运动学追踪
            public Vector3 PrevHandPos;
            public Vector3 Velocity;
            public float Speed;
            public bool HasPrevPos;

            // 上一帧是否处于按键按下状态：抓取只在"按下沿"开始，
            // 否则按住 Grip 的手每帧都会重复做全角色搜索，并且滑过衣物时误触发脱衣。
            public bool WasGrabActive;
        }

        // 常量参数配置
        private const float DefaultPullThreshold = 0.13f;        // 默认拉伸撕扯位移阈值 (0.12~0.15m)
        private const float GrabDetectRadius = 0.22f;            // 掌心抓取探测半径 (米)
        private const float MaxCharacterInteractDistance = 2.5f; // 角色交互最大近身距离 (米)
        private const float SwipeRipSpeedThreshold = 0.85f;      // 快速划过撕扯的速度阈值 (m/s)
        private const float SwipeRipMinDistance = 0.045f;        // 快速划扯所需的最小位移 (米)

        private const int OverlapBufferSize = 128;
        private static readonly Collider[] OverlapBuffer = new Collider[OverlapBufferSize];

        private static VRPhysicalUndresser _instance;
        public static VRPhysicalUndresser Instance => _instance;

        public static bool IsGrabbingAny =>
            _instance != null && (_instance._leftHand.IsGrabbing || _instance._rightHand.IsGrabbing);

        private readonly HandState _leftHand = new HandState { IsLeft = true };
        private readonly HandState _rightHand = new HandState { IsLeft = false };

        private readonly List<AudioClip> _hardClips = new List<AudioClip>();
        private readonly List<AudioClip> _softClips = new List<AudioClip>();
        private bool _audioLoaded;

        private KKCharaStudioVRSettings _settings;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            ResolveSettings();
            StartCoroutine(LoadAudioClipsCo());
        }

        private void OnDisable()
        {
            ResetHand(_leftHand);
            ResetHand(_rightHand);
            _leftHand.WasGrabActive = false;
            _rightHand.WasGrabActive = false;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            // 回退音效是运行时创建的 AudioClip，不会随场景自动释放
            ReleaseClips(_hardClips);
            ReleaseClips(_softClips);
            _audioLoaded = false;
        }

        private static void ReleaseClips(List<AudioClip> clips)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null)
                    Destroy(clips[i]);
            }
            clips.Clear();
        }

        private void ResolveSettings()
        {
            if (VR.Manager != null && VR.Manager.Context != null)
            {
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
            }
        }

        private float PullThreshold =>
            _settings != null && _settings.PhysicalUndresserPullDistance > 0.05f
                ? _settings.PhysicalUndresserPullDistance
                : DefaultPullThreshold;

        private bool IsEnabled =>
            VRStudioInteractionPolicy.PhysicalUndressAllowed(
                VRInteractionOptions.DynamicTouchEnabled,
                VRInteractionOptions.PhysicalUndressEnabled);

        private float SfxVolume =>
            _settings != null ? _settings.PhysicalUndresserSFXVolume : 1.0f;

        private void Update()
        {
            if (!IsEnabled)
            {
                ResetHand(_leftHand);
                ResetHand(_rightHand);
                return;
            }

            if (VR.Mode == null)
                return;

            UpdateHand(_leftHand, VR.Mode.Left);
            UpdateHand(_rightHand, VR.Mode.Right);
        }

        private void ResetHand(HandState hand)
        {
            hand.IsGrabbing = false;
            hand.HasTriggered = false;
            hand.TargetChar = null;
            hand.TargetOci = null;
            hand.TargetPart = UndressPart.None;
            hand.HasPrevPos = false;
            hand.Velocity = Vector3.zero;
            hand.Speed = 0f;
        }

        private void UpdateHand(HandState hand, Controller controller)
        {
            if (controller == null || !controller.gameObject.activeInHierarchy)
            {
                ResetHand(hand);
                hand.WasGrabActive = false;
                return;
            }

            SteamVR_Controller.Device device = ResolveDevice(controller);
            if (device == null || !device.connected)
            {
                ResetHand(hand);
                hand.WasGrabActive = false;
                return;
            }

            // 手部掌心中心位置 (控制器稍前下方位置)
            Vector3 handPos = controller.transform.TransformPoint(0f, -0.03f, 0.06f);

            // 实时运动学速度追踪 (Linear Velocity & Speed)
            float dt = Time.deltaTime;
            if (hand.HasPrevPos && dt > 0.0001f)
            {
                Vector3 instantVel = (handPos - hand.PrevHandPos) / dt;
                if (!float.IsNaN(instantVel.x) && !float.IsInfinity(instantVel.x))
                {
                    hand.Velocity = Vector3.Lerp(hand.Velocity, instantVel, 0.45f);
                    hand.Speed = hand.Velocity.magnitude;
                }
            }
            else
            {
                hand.HasPrevPos = true;
                hand.Velocity = Vector3.zero;
                hand.Speed = 0f;
            }
            hand.PrevHandPos = handPos;

            // Grip 抓握按键 与 Trigger 扳机按键（支持抓握拉扯或扳机快速划扯）
            bool gripHeld = device.GetPress(EVRButtonId.k_EButton_Grip);
            bool gripDown = device.GetPressDown(EVRButtonId.k_EButton_Grip);

            bool triggerHeld = device.GetPress(EVRButtonId.k_EButton_Axis1)
                               || device.GetAxis(EVRButtonId.k_EButton_Axis1).x > 0.45f;
            bool triggerDown = device.GetPressDown(EVRButtonId.k_EButton_Axis1);

            bool grabActive = gripHeld || triggerHeld;
            // OpenXR 可能丢失 PressDown 边沿，所以同时用电平上升沿兜底
            bool grabJustStarted = gripDown || triggerDown || (grabActive && !hand.WasGrabActive);
            hand.WasGrabActive = grabActive;

            if (!hand.IsGrabbing)
            {
                // 未处于抓取中：仅在按下沿检测手部是否接触服装插槽区域
                if (grabJustStarted)
                {
                    // 避让工作室正在拖拽 IK 操纵杆或摆姿势的情况
                    if (GripMoveKKCharaStudioTool.AnyObjectInteractionActive)
                        return;

                    if (TryFindTarget(handPos, out ChaControl targetChar, out OCIChar targetOci, out UndressPart targetPart, out Vector3 anchorPos))
                    {
                        hand.IsGrabbing = true;
                        hand.HasTriggered = false;
                        hand.GrabStartHandPos = handPos;
                        hand.GrabTargetAnchorPos = anchorPos;
                        hand.TargetChar = targetChar;
                        hand.TargetOci = targetOci;
                        hand.TargetPart = targetPart;
                        hand.LastTensionHapticTime = Time.unscaledTime;

                        // 抓住布料瞬间轻微触感提示 (400 微秒脉冲)
                        device.TriggerHapticPulse(400, EVRButtonId.k_EButton_Axis0);
                    }
                }
            }
            else
            {
                // 处于抓取中：玩家松开按键则重置抓取状态
                if (!grabActive)
                {
                    ResetHand(hand);
                    return;
                }

                // 目标角色已失效或正在重载（换卡/换装时衣物数据处于中间状态）
                if (hand.TargetChar == null || !hand.TargetChar.gameObject.activeInHierarchy || !hand.TargetChar.loadEnd)
                {
                    ResetHand(hand);
                    return;
                }

                // 如果本轮抓取已触发过剥离，等待松手以防连续多重触发
                if (hand.HasTriggered)
                    return;

                Vector3 pullDelta = handPos - hand.GrabStartHandPos;
                float pullDistance = pullDelta.magnitude;
                float speed = hand.Speed;

                // 判定是否属于有效的拉扯方向：
                // 1. 向下位移 (Y 轴下移 > 0.035m)
                bool isDownward = pullDelta.y < -0.035f;
                // 2. 向外拉扯 (离身体锚点距离明显增加 > 0.035m)
                float currentDistToAnchor = Vector3.Distance(handPos, hand.GrabTargetAnchorPos);
                float initialDistToAnchor = Vector3.Distance(hand.GrabStartHandPos, hand.GrabTargetAnchorPos);
                bool isOutward = currentDistToAnchor > initialDistToAnchor + 0.035f;

                bool isPullingDirection = isDownward || isOutward || pullDistance >= (PullThreshold + 0.02f);

                // 交互过程中的织物张力震颤反馈 (随拉伸位移递增，呈现纤维受力紧绷感)
                if (pullDistance > 0.035f && Time.unscaledTime - hand.LastTensionHapticTime > 0.05f)
                {
                    hand.LastTensionHapticTime = Time.unscaledTime;
                    float progress = Mathf.Clamp01(pullDistance / PullThreshold);
                    ushort tensionPulse = (ushort)Mathf.Lerp(250f, 1100f, progress);
                    device.TriggerHapticPulse(tensionPulse, EVRButtonId.k_EButton_Axis0);
                }

                // 双模撕衣判定：
                // 模态 A：常规拉拽位移超阈值 (Pull Rip)
                bool pullRipTriggered = pullDistance >= PullThreshold && isPullingDirection;
                // 模态 B：快速挥手/快速划扯 (Swipe Rip: 手速达到阈值且产生有效划过距离)
                bool swipeRipTriggered = speed >= SwipeRipSpeedThreshold && pullDistance >= SwipeRipMinDistance;

                if (pullRipTriggered || swipeRipTriggered)
                {
                    hand.HasTriggered = true;
                    bool undressed = false;
                    try
                    {
                        undressed = ExecuteUndress(hand.TargetChar, hand.TargetOci, hand.TargetPart, handPos, device);
                    }
                    catch (Exception ex)
                    {
                        // 衣物/Studio 状态在抓取期间可能被外部改动（换装、删角色），不能让异常中断 Update
                        VRLog.Warn("[PhysicalUndresser] Undress failed: " + ex.Message);
                    }
                    if (undressed)
                    {
                        string mode = swipeRipTriggered ? "Fast Swipe" : "Stretch Pull";
                        VRLog.Info($"[PhysicalUndresser] Hand {(hand.IsLeft ? "L" : "R")} torn {hand.TargetPart} on {hand.TargetChar.name}! Mode: {mode}, Dist: {pullDistance:F3}m, Speed: {speed:F2}m/s");
                    }
                }
            }
        }

        #region 敏感区域判定与角色匹配

        /// <summary>
        /// 检测手部周围是否有近身角色的常规衣物插槽敏感区域，且该区域有衣物可脱/撕。
        /// 【安全策略】：绝对严禁触碰任何饰品插槽（角色的头发、发饰、呆毛等常作为饰品加载，绝不可被拖拽剥离）。
        /// </summary>
        public bool TryFindTarget(
            Vector3 handPos,
            out ChaControl targetChar,
            out OCIChar targetOci,
            out UndressPart targetPart,
            out Vector3 targetAnchorPos)
        {
            targetChar = null;
            targetOci = null;
            targetPart = UndressPart.None;
            targetAnchorPos = Vector3.zero;

            float closestDistance = GrabDetectRadius;

            Vector3 playerHead = VR.Camera != null && VR.Camera.Head != null
                ? VR.Camera.Head.position
                : handPos;

            // 1. 先通过物理 Collider 检测手柄是否直接触碰到敏感部位 Collider
            int hitCount = Physics.OverlapSphereNonAlloc(
                handPos, GrabDetectRadius, OverlapBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            {
                for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    Collider col = OverlapBuffer[hitIndex];
                    OverlapBuffer[hitIndex] = null;
                    if (col == null || !col.gameObject.activeInHierarchy)
                        continue;

                    ChaControl chara = col.GetComponentInParent<ChaControl>();
                    if (chara == null || !chara.gameObject.activeInHierarchy || !chara.loadEnd || !chara.visibleAll)
                        continue;

                    // 近身距离判定：仅允许触碰玩家身边近距离角色
                    if (Vector3.Distance(playerHead, chara.transform.position) > MaxCharacterInteractDistance)
                        continue;

                    // 严禁判定属于饰品或头发物体的任何 Collider（保护 Mod 发型与饰品绝不被误判为衣物）
                    if (IsAccessoryOrHairCollider(chara, col))
                        continue;

                    UndressPart part = ClassifyCollider(col.name);
                    if (part == UndressPart.None)
                        continue;

                    if (!HasAnyClothedInPart(chara, part))
                        continue;

                    float dist = Vector3.Distance(handPos, col.bounds.ClosestPoint(handPos));
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        targetChar = chara;
                        targetOci = FindOciChar(chara);
                        targetPart = part;
                        targetAnchorPos = col.transform.position;
                    }
                }
            }

            // 绝对严禁任何饰品插槽检测：所有饰品（头发、发饰、头饰、兽耳等）绝不参与脱衣！

            // 2. 如果物理碰撞体未命中，通过角色骨骼参考锚点进行精准距离补算（仅限常规服装）
            foreach (ChaControl chara in GetStudioCharacters())
            {
                if (chara == null || !chara.gameObject.activeInHierarchy || !chara.loadEnd || !chara.visibleAll)
                    continue;

                if (Vector3.Distance(playerHead, chara.transform.position) > MaxCharacterInteractDistance)
                    continue;

                CheckCandidatePart(chara, UndressPart.Top, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
                CheckCandidatePart(chara, UndressPart.Bra, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
                CheckCandidatePart(chara, UndressPart.Bottom, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
                CheckCandidatePart(chara, UndressPart.Panties, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
                CheckCandidatePart(chara, UndressPart.Socks, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
                CheckCandidatePart(chara, UndressPart.Shoes, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
                CheckCandidatePart(chara, UndressPart.Gloves, handPos, ref closestDistance, ref targetChar, ref targetOci, ref targetPart, ref targetAnchorPos);
            }

            return targetChar != null && targetPart != UndressPart.None;
        }

        private void CheckCandidatePart(
            ChaControl chara,
            UndressPart part,
            Vector3 handPos,
            ref float closestDistance,
            ref ChaControl targetChar,
            ref OCIChar targetOci,
            ref UndressPart targetPart,
            ref Vector3 targetAnchorPos)
        {
            if (!HasAnyClothedInPart(chara, part))
                return;

            Vector3 partPos;
            if (TryGetPartWorldPosition(chara, part, handPos, out partPos))
            {
                float dist = Vector3.Distance(handPos, partPos);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    targetChar = chara;
                    targetOci = FindOciChar(chara);
                    targetPart = part;
                    targetAnchorPos = partPos;
                }
            }
        }

        private static UndressPart ClassifyCollider(string colName)
        {
            if (string.IsNullOrEmpty(colName))
                return UndressPart.None;

            string lower = colName.ToLowerInvariant();

            // 排除可能包含在名称中的饰品/头发关键字
            if (lower.Contains("hair") || lower.Contains("acc") || lower.Contains("acs") || lower.Contains("head"))
                return UndressPart.None;

            // 上衣 / 胸部 / Bra
            if (lower.Contains("bust") || lower.Contains("mune") || lower.Contains("breast") || lower.Contains("spine03"))
                return UndressPart.Top;

            // 下装 / 短裙 / 腹部
            if (lower.Contains("waist") || lower.Contains("berry") || lower.Contains("spine01"))
                return UndressPart.Bottom;

            // 内裤 / 裆部 / 臀部
            if (lower.Contains("kokan") || lower.Contains("ana") || lower.Contains("siri") || lower.Contains("groin") || lower.Contains("asoko"))
                return UndressPart.Panties;

            // 鞋子
            if (lower.Contains("foot") || lower.Contains("leg02") || lower.Contains("shoes"))
                return UndressPart.Shoes;

            // 丝袜 / 短袜 / 腿部
            if (lower.Contains("thigh") || lower.Contains("leg"))
                return UndressPart.Socks;

            // 手套 / 手腕 / 手臂
            if (lower.Contains("wrist") || lower.Contains("hand") || lower.Contains("arm"))
                return UndressPart.Gloves;

            return UndressPart.None;
        }

        /// <summary>
        /// 判定 Collider 是否属于饰品插槽物体或发型物体。
        /// 恋活中大量发型、呆毛、头饰挂在饰品插槽（objAccessory）或原生发型（objHair）上，严禁将其识别为衣物碰撞体。
        /// </summary>
        private static bool IsAccessoryOrHairCollider(ChaControl chara, Collider col)
        {
            if (chara == null || col == null) return false;

            // 1. 饰品组件识别
            if (col.GetComponentInParent<ChaAccessoryComponent>() != null)
                return true;

            Transform colT = col.transform;

            // 2. 动态饰品插槽物体树级检测
            if (chara.objAccessory != null)
            {
                for (int i = 0; i < chara.objAccessory.Length; i++)
                {
                    GameObject accObj = chara.objAccessory[i];
                    if (accObj != null && colT.IsChildOf(accObj.transform))
                        return true;
                }
            }

            // 3. 原生发型插槽物体树级检测
            if (chara.objHair != null)
            {
                for (int i = 0; i < chara.objHair.Length; i++)
                {
                    GameObject hairObj = chara.objHair[i];
                    if (hairObj != null && colT.IsChildOf(hairObj.transform))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetPartWorldPosition(ChaControl chara, UndressPart part, Vector3 handPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (chara == null) return false;

            switch (part)
            {
                case UndressPart.Top:
                case UndressPart.Bra:
                {
                    GameObject bustF = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_bust_f);
                    if (bustF != null) { worldPos = bustF.transform.position; return true; }
                    GameObject bust = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_bust);
                    if (bust != null) { worldPos = bust.transform.position; return true; }
                    Transform spine3 = chara.transform.Find("cf_J_Root/cf_N_height/cf_J_Hips/cf_J_Kosi01/cf_J_Kosi02/cf_J_Spine01/cf_J_Spine02/cf_J_Spine03");
                    if (spine3 != null) { worldPos = spine3.position + spine3.forward * 0.12f; return true; }
                    break;
                }

                case UndressPart.Bottom:
                {
                    GameObject waist = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_waist);
                    if (waist != null) { worldPos = waist.transform.position; return true; }
                    GameObject waistF = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_waist_f);
                    if (waistF != null) { worldPos = waistF.transform.position; return true; }
                    Transform kosi = chara.transform.Find("cf_J_Root/cf_N_height/cf_J_Hips/cf_J_Kosi01");
                    if (kosi != null) { worldPos = kosi.position; return true; }
                    break;
                }

                case UndressPart.Panties:
                {
                    GameObject kokan = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_kokan);
                    if (kokan != null) { worldPos = kokan.transform.position; return true; }
                    GameObject ana = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_ana);
                    if (ana != null) { worldPos = ana.transform.position; return true; }
                    Transform hips = chara.transform.Find("cf_J_Root/cf_N_height/cf_J_Hips");
                    if (hips != null) { worldPos = hips.position - hips.up * 0.08f; return true; }
                    break;
                }

                case UndressPart.Socks:
                {
                    GameObject kneeL = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_knee_L);
                    GameObject kneeR = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_knee_R);
                    if (kneeL != null && kneeR != null)
                    {
                        float dL = Vector3.Distance(handPos, kneeL.transform.position);
                        float dR = Vector3.Distance(handPos, kneeR.transform.position);
                        worldPos = dL < dR ? kneeL.transform.position : kneeR.transform.position;
                        return true;
                    }
                    if (kneeL != null) { worldPos = kneeL.transform.position; return true; }
                    break;
                }

                case UndressPart.Shoes:
                {
                    GameObject ankleL = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_ankle_L);
                    GameObject ankleR = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_ankle_R);
                    if (ankleL != null && ankleR != null)
                    {
                        float dL = Vector3.Distance(handPos, ankleL.transform.position);
                        float dR = Vector3.Distance(handPos, ankleR.transform.position);
                        worldPos = dL < dR ? ankleL.transform.position : ankleR.transform.position;
                        return true;
                    }
                    if (ankleL != null) { worldPos = ankleL.transform.position; return true; }
                    break;
                }

                case UndressPart.Gloves:
                {
                    GameObject wristL = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_wrist_L);
                    GameObject wristR = chara.GetReferenceInfo(ChaReference.RefObjKey.a_n_wrist_R);
                    if (wristL != null && wristR != null)
                    {
                        float dL = Vector3.Distance(handPos, wristL.transform.position);
                        float dR = Vector3.Distance(handPos, wristR.transform.position);
                        worldPos = dL < dR ? wristL.transform.position : wristR.transform.position;
                        return true;
                    }
                    if (wristL != null) { worldPos = wristL.transform.position; return true; }
                    break;
                }
            }

            return false;
        }

        #endregion

        #region 衣物状态推进与撕衣服执行

        /// <summary>
        /// 判定指定常规服装敏感区域是否还有未脱完的衣物（绝对不判定饰品）
        /// </summary>
        private static bool HasAnyClothedInPart(ChaControl chara, UndressPart part)
        {
            if (chara == null) return false;

            switch (part)
            {
                case UndressPart.Top:
                    return IsSlotClothed(chara, 0) || IsSlotClothed(chara, 2);

                case UndressPart.Bra:
                    return IsSlotClothed(chara, 2);

                case UndressPart.Bottom:
                    return IsSlotClothed(chara, 1) || IsSlotClothed(chara, 3);

                case UndressPart.Panties:
                    return (IsSlotClothed(chara, 1) && chara.fileStatus.clothesState[1] == 0)
                           || IsSlotClothed(chara, 5)
                           || IsSlotClothed(chara, 3);

                case UndressPart.Socks:
                    return IsSlotClothed(chara, 5) || IsSlotClothed(chara, 6);

                case UndressPart.Shoes:
                    return IsSlotClothed(chara, 8) || IsSlotClothed(chara, 7);

                case UndressPart.Gloves:
                    return IsSlotClothed(chara, 4) || IsSlotClothed(chara, 0);

                default:
                    return false;
            }
        }

        public static bool IsSlotClothed(ChaControl chara, int slot)
        {
            if (chara == null || slot < 0) return false;
            if (chara.fileStatus == null || chara.fileStatus.clothesState == null || slot >= chara.fileStatus.clothesState.Length) return false;
            try
            {
                if (!chara.IsClothes(slot)) return false;
            }
            catch
            {
                // 角色正在重载/衣物对象已被销毁
                return false;
            }

            byte current = chara.fileStatus.clothesState[slot];
            int max = GetMaxUndressedState(chara, slot);
            return current < max;
        }

        public static int GetMaxUndressedState(ChaControl chara, int slot)
        {
            try
            {
                var kinds = chara.GetClothesStateKind(slot);
                if (kinds == null || kinds.Count == 0) return 2;
                int max = 0;
                foreach (byte k in kinds.Keys)
                {
                    if (k > max) max = k;
                }
                return max > 0 ? max : 2;
            }
            catch
            {
                return 2;
            }
        }

        /// <summary>
        /// 执行徒手物理撕衣/脱衣核心逻辑：
        /// 按照现实物理层次推进剥离，播放音效、震动手柄并联动工作室面板。
        /// 【严正安全保护】：绝对严禁触碰任何饰品插槽（角色的头发、发饰、兽耳等常位于饰品槽），只允许推进常规服装。
        /// </summary>
        private bool ExecuteUndress(ChaControl chara, OCIChar oci, UndressPart part, Vector3 ripPosition, SteamVR_Controller.Device device)
        {
            if (!IsEnabled)
                return false;
            if (chara == null) return false;

            int changedSlot = -1;
            bool isHardSound = true;

            // 绝对严禁触碰任何饰品插槽！物理撕衣服系统只能且仅能作用于常规服装插槽。
            switch (part)
            {
                case UndressPart.Top:
                {
                    // 优先级：先外套上衣(0)，后胸罩(2)
                    if (IsSlotClothed(chara, 0))
                    {
                        changedSlot = 0;
                        AdvanceSlotState(chara, oci, 0);
                        isHardSound = true;
                    }
                    else if (IsSlotClothed(chara, 2))
                    {
                        changedSlot = 2;
                        AdvanceSlotState(chara, oci, 2);
                        isHardSound = false;
                    }
                    break;
                }

                case UndressPart.Bra:
                {
                    // 若上衣仍穿着完整(0)，先将上衣敞开/脱下
                    if (IsSlotClothed(chara, 0) && chara.fileStatus.clothesState[0] == 0)
                    {
                        changedSlot = 0;
                        AdvanceSlotState(chara, oci, 0);
                        isHardSound = true;
                    }
                    else if (IsSlotClothed(chara, 2))
                    {
                        changedSlot = 2;
                        AdvanceSlotState(chara, oci, 2);
                        isHardSound = false;
                    }
                    break;
                }

                case UndressPart.Bottom:
                {
                    // 优先级：先下装/短裙(1)，后内裤(3)
                    if (IsSlotClothed(chara, 1))
                    {
                        changedSlot = 1;
                        AdvanceSlotState(chara, oci, 1);
                        isHardSound = true;
                    }
                    else if (IsSlotClothed(chara, 3))
                    {
                        changedSlot = 3;
                        AdvanceSlotState(chara, oci, 3);
                        isHardSound = false;
                    }
                    break;
                }

                case UndressPart.Panties:
                {
                    // 物理层次关系：若外装裙子还穿好(0)，先褪下裙子
                    if (IsSlotClothed(chara, 1) && chara.fileStatus.clothesState[1] == 0)
                    {
                        changedSlot = 1;
                        AdvanceSlotState(chara, oci, 1);
                        isHardSound = true;
                    }
                    else if (IsSlotClothed(chara, 5)) // 裤袜 / 丝袜
                    {
                        changedSlot = 5;
                        AdvanceSlotState(chara, oci, 5);
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 3)) // 内裤
                    {
                        changedSlot = 3;
                        AdvanceSlotState(chara, oci, 3);
                        isHardSound = false;
                    }
                    break;
                }

                case UndressPart.Shoes:
                {
                    // 剥离鞋子：室外鞋(8) 与 室内鞋(7)
                    int shoesSlot = (chara.fileStatus != null && chara.fileStatus.shoesType == 0) ? 7 : 8;
                    if (IsSlotClothed(chara, shoesSlot))
                    {
                        changedSlot = shoesSlot;
                        SetSlotState(chara, oci, shoesSlot, (byte)GetMaxUndressedState(chara, shoesSlot));
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 8))
                    {
                        changedSlot = 8;
                        SetSlotState(chara, oci, 8, (byte)GetMaxUndressedState(chara, 8));
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 7))
                    {
                        changedSlot = 7;
                        SetSlotState(chara, oci, 7, (byte)GetMaxUndressedState(chara, 7));
                        isHardSound = false;
                    }
                    break;
                }

                case UndressPart.Socks:
                {
                    // 优先级：先鞋子(8/7) -> 裤袜/丝袜(5) -> 短袜(6)
                    if (IsSlotClothed(chara, 8))
                    {
                        changedSlot = 8;
                        SetSlotState(chara, oci, 8, (byte)GetMaxUndressedState(chara, 8));
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 7))
                    {
                        changedSlot = 7;
                        SetSlotState(chara, oci, 7, (byte)GetMaxUndressedState(chara, 7));
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 5))
                    {
                        changedSlot = 5;
                        AdvanceSlotState(chara, oci, 5);
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 6))
                    {
                        changedSlot = 6;
                        AdvanceSlotState(chara, oci, 6);
                        isHardSound = false;
                    }
                    break;
                }

                case UndressPart.Gloves:
                {
                    // 优先级：手套(4) -> 上衣袖套(0)
                    if (IsSlotClothed(chara, 4))
                    {
                        changedSlot = 4;
                        AdvanceSlotState(chara, oci, 4);
                        isHardSound = false;
                    }
                    else if (IsSlotClothed(chara, 0))
                    {
                        changedSlot = 0;
                        AdvanceSlotState(chara, oci, 0);
                        isHardSound = true;
                    }
                    break;
                }
            }

            if (changedSlot >= 0)
            {
                // 1. 播放空间 3D 撕衣服音效 (Hard: 外套/裙子; Soft: 内衣/丝袜)
                PlayUndressSound(isHardSound, ripPosition);

                // 2. 触发手柄多段物理阻尼冲击触觉震颤
                if (device != null)
                {
                    TriggerRipHaptic(device);
                }

                // 3. 联动工作室衣物面板即时刷新
                if (oci != null && oci.charInfo == chara)
                {
                    VRCharacterClothingService.RefreshStudioCharacterPanel(oci);
                }

                return true;
            }

            return false;
        }

        private static void AdvanceSlotState(ChaControl chara, OCIChar oci, int slot)
        {
            if (!IsSlotClothed(chara, slot)) return;

            byte currentState = chara.fileStatus.clothesState[slot];
            int maxState = GetMaxUndressedState(chara, slot);
            var kinds = chara.GetClothesStateKind(slot);

            byte nextState = (byte)maxState;

            if (kinds != null && kinds.Count > 0)
            {
                // 寻找严格大于当前状态的最小可用阶段（完整 -> 半脱/敞开 -> 全脱）
                byte candidate = 255;
                foreach (byte k in kinds.Keys)
                {
                    if (k > currentState && k < candidate)
                    {
                        candidate = k;
                    }
                }
                if (candidate != 255)
                {
                    nextState = candidate;
                }
            }

            SetSlotState(chara, oci, slot, nextState);
        }

        private static void SetSlotState(ChaControl chara, OCIChar oci, int slot, byte targetState)
        {
            // 抓取期间 OCIChar 可能已被删除（charInfo 置空）或指向另一个角色，此时回退到直接操作 ChaControl
            if (oci != null && (oci.charInfo == null || oci.charInfo != chara))
                oci = null;

            if (oci != null)
            {
                oci.SetClothesState(slot, targetState);
            }
            else if (chara != null)
            {
                chara.SetClothesState(slot, targetState, true);
            }
        }

        #endregion

        #region 音效加载与播放

        private IEnumerator LoadAudioClipsCo()
        {
            string sfxDir = ResolveSfxDirectory();
            if (string.IsNullOrEmpty(sfxDir) || !Directory.Exists(sfxDir))
            {
                VRLog.Warn("[PhysicalUndresser] SFX directory not found: " + sfxDir);
                yield break;
            }

            string[] files = Directory.GetFiles(sfxDir, "*.wav");
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                bool match = fileName.IndexOf("Undress", StringComparison.OrdinalIgnoreCase) >= 0
                             || fileName.IndexOf("strip", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) continue;

                bool isHard = fileName.IndexOf("Hard", StringComparison.OrdinalIgnoreCase) >= 0
                              || fileName.IndexOf("strip_fabric_hard", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSoft = fileName.IndexOf("Soft", StringComparison.OrdinalIgnoreCase) >= 0
                              || fileName.IndexOf("strip_fabric_soft", StringComparison.OrdinalIgnoreCase) >= 0;

                string fileUri = "file:///" + file.Replace('\\', '/');
                using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(fileUri, AudioType.WAV))
                {
                    yield return uwr.SendWebRequest();
                    if (!uwr.isHttpError && !uwr.isNetworkError)
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
                        if (clip == null)
                            continue;
                        clip.name = fileName;
                        if (isHard)
                            _hardClips.Add(clip);
                        else if (isSoft)
                            _softClips.Add(clip);
                        else
                            _softClips.Add(clip);
                    }
                    else
                    {
                        VRLog.Warn($"[PhysicalUndresser] Failed to load audio {file}: {uwr.error}");
                    }
                }
            }

            _audioLoaded = true;
            VRLog.Info($"[PhysicalUndresser] Fallback Audio loaded: {_hardClips.Count} Hard clips, {_softClips.Count} Soft clips.");
        }

        private static string ResolveSfxDirectory()
        {
            string localPath = Path.Combine(Directory.GetCurrentDirectory(), "UserData", "VR", "SFX");
            if (Directory.Exists(localPath)) return localPath;

            try
            {
                string dataRelative = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserData", "VR", "SFX"));
                if (Directory.Exists(dataRelative)) return dataRelative;
            }
            catch { }

            try
            {
                string bepRoot = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "UserData", "VR", "SFX"));
                if (Directory.Exists(bepRoot)) return bepRoot;
            }
            catch { }

            string absolute = @"E:\KKS\KoikatuSunshine\UserData\VR\SFX";
            if (Directory.Exists(absolute)) return absolute;

            return localPath;
        }

        private void PlayUndressSound(bool isHard, Vector3 position)
        {
            float volume = Mathf.Clamp01(SfxVolume);
            var intensity = isHard ? VRSFXService.Intensity.Hard : VRSFXService.Intensity.Soft;

            // 优先通过全局音频池服务 VRSFXService 播放
            if (VRSFXService.Instance != null && VRSFXService.Instance.IsLoaded)
            {
                float duration = VRSFXService.Instance.Play(
                    VRSFXService.Sfx.Undress,
                    VRSFXService.Surface.Cloth,
                    intensity,
                    position,
                    volume);

                if (duration > 0.05f)
                    return;
            }

            // 独立后备音效播放
            if (!_audioLoaded) return;
            List<AudioClip> pool = isHard ? _hardClips : _softClips;
            if (pool.Count == 0) pool = isHard ? _softClips : _hardClips;
            if (pool.Count == 0) return;

            AudioClip clip = pool[UnityEngine.Random.Range(0, pool.Count)];
            if (clip == null) return;

            GameObject sfxHost = new GameObject("VRUndressAudio_" + clip.name);
            sfxHost.transform.position = position;

            AudioSource source = sfxHost.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 0.85f; // 3D 空间立体声音效
            source.pitch = 0.94f + UnityEngine.Random.value * 0.12f; // 微量随机音高
            source.Play();

            Destroy(sfxHost, clip.length + 0.25f);
        }

        #endregion

        #region 触感震颤反馈 (Haptics)

        private void TriggerRipHaptic(SteamVR_Controller.Device device)
        {
            StartCoroutine(RipHapticPatternCo(device));
        }

        private static IEnumerator RipHapticPatternCo(SteamVR_Controller.Device device)
        {
            if (device == null || !device.connected) yield break;

            // 仿织物纤维断裂阻尼波形：强冲击脉冲 -> 次级释放 -> 余震消退
            device.TriggerHapticPulse(2800, EVRButtonId.k_EButton_Axis0);
            yield return new WaitForSeconds(0.025f);
            if (device.connected)
                device.TriggerHapticPulse(1800, EVRButtonId.k_EButton_Axis0);
            yield return new WaitForSeconds(0.025f);
            if (device.connected)
                device.TriggerHapticPulse(900, EVRButtonId.k_EButton_Axis0);
        }

        #endregion

        #region 辅助工具方法

        private static SteamVR_Controller.Device ResolveDevice(Controller controller)
        {
            if (controller == null) return null;

            SteamVR_TrackedObject tracked = controller.GetComponent<SteamVR_TrackedObject>();
            if (tracked != null && tracked.index != SteamVR_TrackedObject.EIndex.None)
            {
                var dev = SteamVR_Controller.Input((int)tracked.index);
                if (dev != null) return dev;
            }

#if KKS
            return SteamVR_Controller.ForController(controller);
#else
            return null;
#endif
        }

        public static OCIChar FindOciChar(ChaControl chara)
        {
            if (chara == null) return null;
            Studio.Studio studio = Singleton<Studio.Studio>.Instance;
            if (studio?.dicObjectCtrl == null) return null;

            foreach (var pair in studio.dicObjectCtrl)
            {
                if (pair.Value is OCIChar oci && oci.charInfo == chara)
                    return oci;
            }
            return null;
        }

        public static IEnumerable<ChaControl> GetStudioCharacters()
        {
            IEnumerable<ChaControl> chars = null;
            try
            {
                chars = VRGameCompatibility.Characters;
            }
            catch { }

            if (chars != null)
            {
                foreach (ChaControl c in chars)
                {
                    if (c != null && c.gameObject.activeInHierarchy && c.loadEnd)
                        yield return c;
                }
                yield break;
            }

            var studio = Singleton<Studio.Studio>.Instance;
            if (studio?.dicObjectCtrl != null)
            {
                foreach (var pair in studio.dicObjectCtrl)
                {
                    if (pair.Value is OCIChar oci && oci.charInfo != null && oci.charInfo.gameObject.activeInHierarchy && oci.charInfo.loadEnd)
                        yield return oci.charInfo;
                }
            }
        }

        #endregion
    }
}
