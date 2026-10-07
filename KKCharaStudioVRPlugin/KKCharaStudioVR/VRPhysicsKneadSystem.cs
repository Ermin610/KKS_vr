using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;
using VRGIN.Controls;

namespace KKCharaStudioVR
{
    /// <summary>
    /// 物理揉捏 + 肌肉受力回弹系统 (Physics Kneading & Muscle Restitution System)
    /// 为工作室 VR 角色提供高拟真软体（乳房、臀部等）实时抓握揉捏、挤压形变（Squash & Stretch 体积守恒）、
    /// 动态阻尼触觉震动反馈，以及松手后的二阶弹簧-阻尼阻抗振荡（Damped Harmonic Oscillator）自然回弹与 DynamicBone 无缝过渡。
    /// </summary>
    public class VRPhysicsKneadManager : MonoBehaviour
    {
        private static VRPhysicsKneadManager _instance;
        public static VRPhysicsKneadManager Instance => _instance;

        private readonly Dictionary<ChaControl, VRKneadTarget> _targets = new Dictionary<ChaControl, VRKneadTarget>();
        private readonly List<ChaControl> _deadCharaList = new List<ChaControl>();
        private readonly List<ChaControl> _scanCharas = new List<ChaControl>(8);

        private VRHandKneadController _leftHandKnead;
        private VRHandKneadController _rightHandKnead;

        private KKCharaStudioVRSettings _settings;
        private float _nextScanTime;

        public bool IsEnabled =>
            VRStudioInteractionPolicy.KneadTouchAllowed(
                VRInteractionOptions.DynamicTouchEnabled,
                VRInteractionOptions.PhysicalUndressEnabled);
        public float HapticIntensity => _settings != null ? _settings.HapticFeedbackIntensity : 0.5f;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            ResolveSettings();
            StartCoroutine(InitControllersCo());
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void ResolveSettings()
        {
            if (VR.Manager != null && VR.Manager.Context != null)
            {
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
            }
        }

        private IEnumerator InitControllersCo()
        {
            // 持续巡检：VR 模式重建（场景切换/手柄重连）后手柄对象会被替换，
            // 旧的 VRHandKneadController 随之销毁，需要重新挂载。
            WaitForSeconds wait = new WaitForSeconds(1f);
            while (true)
            {
                if (VR.Mode != null)
                {
                    if (_leftHandKnead == null && VR.Mode.Left != null)
                    {
                        _leftHandKnead = VR.Mode.Left.gameObject.AddComponent<VRHandKneadController>();
                        _leftHandKnead.Init(isLeft: true);
                    }

                    if (_rightHandKnead == null && VR.Mode.Right != null)
                    {
                        _rightHandKnead = VR.Mode.Right.gameObject.AddComponent<VRHandKneadController>();
                        _rightHandKnead.Init(isLeft: false);
                    }
                }
                yield return wait;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextScanTime)
            {
                _nextScanTime = Time.unscaledTime + 1.5f;
                ScanCharacters();
            }
        }

        private void ScanCharacters()
        {
            // 清理已销毁/停用/骨架被重建（重载角色）的目标。
            // ChaControl 被 Unity 销毁后 Dictionary 的键仍是同一个托管对象，
            // 所以必须在这里按 Unity 空判定剔除，否则会长期残留已销毁骨骼引用。
            _deadCharaList.Clear();
            foreach (var kvp in _targets)
            {
                ChaControl key = kvp.Key;
                VRKneadTarget value = kvp.Value;
                if (key == null || value == null || !key.gameObject.activeInHierarchy || value.IsStale(key))
                {
                    _deadCharaList.Add(key);
                }
            }
            for (int i = 0; i < _deadCharaList.Count; i++)
            {
                ChaControl dead = _deadCharaList[i];
                VRKneadTarget deadTarget;
                if (_targets.TryGetValue(dead, out deadTarget) && deadTarget != null)
                    deadTarget.ReleaseAllParts();
                _targets.Remove(dead);
            }
            _deadCharaList.Clear();

            // 查找场景中现存角色并挂载 VRKneadTarget。
            // 优先走角色注册表：FindObjectsOfType 遍历整个场景，每 1.5 秒一次会造成 VR 周期性掉帧。
            _scanCharas.Clear();
            try
            {
                foreach (ChaControl chara in VRPhysicalUndresser.GetStudioCharacters())
                    _scanCharas.Add(chara);
            }
            catch (Exception)
            {
                _scanCharas.Clear();
            }
            if (_scanCharas.Count == 0)
                _scanCharas.AddRange(FindObjectsOfType<ChaControl>());

            for (int i = 0; i < _scanCharas.Count; i++)
            {
                ChaControl chara = _scanCharas[i];
                if (chara == null || chara.objBodyBone == null) continue;

                if (!_targets.ContainsKey(chara))
                {
                    GameObject targetObj = chara.objAnim != null ? chara.objAnim : chara.gameObject;
                    var target = targetObj.GetComponent<VRKneadTarget>();
                    if (target == null)
                    {
                        target = targetObj.AddComponent<VRKneadTarget>();
                    }
                    target.Initialize(chara);
                    _targets[chara] = target;
                }
            }
            _scanCharas.Clear();
        }

        public VRKneadTarget GetTarget(ChaControl chara)
        {
            if (chara != null && _targets.TryGetValue(chara, out var target))
                return target;
            return null;
        }

        // 返回具体的 ValueCollection，避免 foreach 时接口装箱枚举器产生每次抓取的 GC 分配
        public Dictionary<ChaControl, VRKneadTarget>.ValueCollection AllTargets => _targets.Values;
    }

    /// <summary>
    /// 角色软体物理揉捏代理（挂载于角色），管理乳房与臀部的各骨骼链及回弹求解器
    /// 执行时序晚于原生 DynamicBone，保证在 LateUpdate 中实时注入变形与阻尼回弹
    /// </summary>
    [DefaultExecutionOrder(25000)]
    public class VRKneadTarget : MonoBehaviour
    {
        public enum PartType
        {
            BustL,
            BustR,
            ButtL,
            ButtR
        }

        public ChaControl Character { get; private set; }
        private GameObject _boundBodyBone;
        private readonly List<KneadBodyPart> _parts = new List<KneadBodyPart>();
        public IReadOnlyList<KneadBodyPart> Parts => _parts;

        /// <summary>
        /// 角色被重载（objBodyBone 被重建）或骨骼已销毁时，缓存的部位全部失效，需要重新 Initialize。
        /// </summary>
        public bool IsStale(ChaControl chara)
        {
            if (chara == null || chara.objBodyBone == null)
                return true;
            if (chara != Character)
                return true;
            if (_boundBodyBone == null || _boundBodyBone != chara.objBodyBone)
                return true;
            for (int i = 0; i < _parts.Count; i++)
            {
                KneadBodyPart part = _parts[i];
                if (part == null || part.RootBone == null)
                    return true;
            }
            return false;
        }

        public void ReleaseAllParts()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                KneadBodyPart part = _parts[i];
                if (part != null && part.CurrentState != KneadBodyPart.State.Idle)
                    part.FinishRestitution();
            }
        }

        public void Initialize(ChaControl chara)
        {
            ReleaseAllParts();
            Character = chara;
            _boundBodyBone = chara != null ? chara.objBodyBone : null;
            _parts.Clear();
            if (chara == null || chara.objBodyBone == null)
                return;

            // 构建左右胸部骨骼链
            var bustL = SetupBustPart(chara, isLeft: true);
            if (bustL != null) _parts.Add(bustL);

            var bustR = SetupBustPart(chara, isLeft: false);
            if (bustR != null) _parts.Add(bustR);

            // 构建左右臀部骨骼链
            var buttL = SetupButtPart(chara, isLeft: true);
            if (buttL != null) _parts.Add(buttL);

            var buttR = SetupButtPart(chara, isLeft: false);
            if (buttR != null) _parts.Add(buttR);
        }

        private KneadBodyPart SetupBustPart(ChaControl chara, bool isLeft)
        {
            string suffix = isLeft ? "_L" : "_R";
            string rootName = "cf_j_bust01" + suffix;
            string midName = "cf_j_bust02" + suffix;
            string tipName = "cf_j_bust03" + suffix;
            string poiName = isLeft ? "k_f_mune03L_02" : "k_f_mune03R_02";

            Transform rootBone = FindBone(chara.objBodyBone, rootName);
            Transform midBone = FindBone(chara.objBodyBone, midName);
            Transform tipBone = FindBone(chara.objBodyBone, tipName);
            Transform poi = FindBone(chara.objBodyBone, poiName);

            if (rootBone == null) return null;

            Transform parentRef = rootBone.parent;
            Transform anchor = poi != null ? poi : (midBone != null ? midBone : rootBone);

            var part = new KneadBodyPart(
                isLeft ? PartType.BustL : PartType.BustR,
                chara,
                rootBone,
                midBone,
                tipBone,
                parentRef,
                anchor,
                isBust: true,
                isLeft: isLeft
            );
            part.FindAndBindDynamicBone();
            return part;
        }

        private KneadBodyPart SetupButtPart(ChaControl chara, bool isLeft)
        {
            string suffix = isLeft ? "_L" : "_R";
            string rootName = "cf_j_siri" + suffix;
            Transform rootBone = FindBone(chara.objBodyBone, rootName);
            if (rootBone == null)
            {
                rootBone = FindBone(chara.objBodyBone, "cf_d_siri01" + suffix);
            }
            if (rootBone == null) return null;

            Transform parentRef = rootBone.parent;
            Transform anchor = rootBone;

            var part = new KneadBodyPart(
                isLeft ? PartType.ButtL : PartType.ButtR,
                chara,
                rootBone,
                midBone: null,
                tipBone: null,
                parentRef,
                anchor,
                isBust: false,
                isLeft: isLeft
            );
            part.FindAndBindDynamicBone();
            return part;
        }

        private static Transform FindBone(GameObject rootObj, string boneName)
        {
            if (rootObj == null) return null;
            var transforms = rootObj.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(transforms[i].name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return transforms[i];
                }
            }
            return null;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _parts.Count; i++)
            {
                _parts[i].OnLateUpdate(dt);
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].CurrentState != KneadBodyPart.State.Idle)
                {
                    _parts[i].FinishRestitution();
                }
            }
        }
    }

    /// <summary>
    /// 软体可揉捏部位的物理状态机与形变求解器
    /// 负责捕获静止位姿（Rest Pose）、计算基于骨骼渐变位移的平滑形变（严格杜绝局部 Scale 几何畸变），
    /// 以及受力释放后的二阶阻尼振荡（Damped Harmonic Oscillator）自然 Q 弹回弹与 DynamicBone 无缝过渡
    /// </summary>
    public class KneadBodyPart
    {
        public enum State
        {
            Idle,
            Kneading,
            Restitution
        }

        public VRKneadTarget.PartType PartType { get; }
        public ChaControl Character { get; }
        public Transform RootBone { get; }
        public Transform MidBone { get; }
        public Transform TipBone { get; }
        public Transform ParentRef { get; }
        public Transform Anchor { get; }
        public bool IsBust { get; }
        public bool IsLeft { get; }

        public State CurrentState { get; private set; } = State.Idle;

        // 初始静止位姿 (在 ParentRef 本地坐标系下)
        private Vector3 _rootRestLocalPos;
        private Quaternion _rootRestLocalRot;

        private Vector3 _midRestLocalPos;
        private Quaternion _midRestLocalRot;

        private Vector3 _tipRestLocalPos;
        private Quaternion _tipRestLocalRot;

        // 揉捏状态数据
        private Vector3 _handRestLocalPos;
        private Quaternion _handRestLocalRot;
        private Vector3 _currentDisplacement;
        private Quaternion _currentRotOffset = Quaternion.identity;
        private float _currentUniformScale = 1.0f;
        private float _currentCompression; // 压缩形变量 epsilon

        // 回弹动力学参数 (Damped Harmonic Oscillator)
        private float _restitutionTime;
        private float _restitutionDuration;
        private Vector3 _initialDisplacement;
        private Vector3 _initialVelocity;
        private Quaternion _initialRotOffset = Quaternion.identity;
        private float _initialUniformScaleOffset;
        private const float NaturalFrequency = 25.13f;   // 固有角频率 wn = 2*pi*fn, fn ≈ 4.0 Hz
        private const float DampingRatio = 0.28f;        // 欠阻尼比 zeta ≈ 0.28，确保 2~3 个自然衰减周期
        private readonly float DampedFrequency;          // wd = wn * sqrt(1 - zeta^2)

        // DynamicBone 反射与控制
        private MonoBehaviour _dynamicBone;
        private FieldInfo _fieldWeight;
        private FieldInfo _fieldParticles;
        private FieldInfo _fieldPos;
        private FieldInfo _fieldPrevPos;
        private FieldInfo _fieldParticleTransform;
        private MethodInfo _methodInitTransforms;
        private float _originalDbWeight = 1.0f;
        private bool _dbLayoutInitialized;

        public KneadBodyPart(
            VRKneadTarget.PartType type,
            ChaControl chara,
            Transform rootBone,
            Transform midBone,
            Transform tipBone,
            Transform parentRef,
            Transform anchor,
            bool isBust,
            bool isLeft)
        {
            PartType = type;
            Character = chara;
            RootBone = rootBone;
            MidBone = midBone;
            TipBone = tipBone;
            ParentRef = parentRef != null ? parentRef : (rootBone.parent != null ? rootBone.parent : rootBone);
            Anchor = anchor;
            IsBust = isBust;
            IsLeft = isLeft;

            DampedFrequency = NaturalFrequency * Mathf.Sqrt(Mathf.Max(0.001f, 1f - DampingRatio * DampingRatio));
            CacheRestPose();
        }

        public void CacheRestPose()
        {
            if (RootBone != null && ParentRef != null)
            {
                _rootRestLocalPos = ParentRef.InverseTransformPoint(RootBone.position);
                _rootRestLocalRot = Quaternion.Inverse(ParentRef.rotation) * RootBone.rotation;
                RootBone.localScale = Vector3.one;
            }

            if (MidBone != null && ParentRef != null)
            {
                _midRestLocalPos = ParentRef.InverseTransformPoint(MidBone.position);
                _midRestLocalRot = Quaternion.Inverse(ParentRef.rotation) * MidBone.rotation;
                MidBone.localScale = Vector3.one;
            }

            if (TipBone != null && ParentRef != null)
            {
                _tipRestLocalPos = ParentRef.InverseTransformPoint(TipBone.position);
                _tipRestLocalRot = Quaternion.Inverse(ParentRef.rotation) * TipBone.rotation;
                TipBone.localScale = Vector3.one;
            }
        }

        public void FindAndBindDynamicBone()
        {
            if (Character == null || Character.objBodyBone == null) return;

            // 优先通过恋活原生 ChaControl.getDynamicBoneBust 精确绑定 (0: BustL, 1: BustR, 2: ButtL, 3: ButtR)
            try
            {
                var method = typeof(ChaControl).GetMethod("getDynamicBoneBust", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    int kind = (int)PartType;
                    var db = method.Invoke(Character, new object[] { kind }) as MonoBehaviour;
                    if (db != null)
                    {
                        BindDynamicBone(db);
                        return;
                    }
                }
            }
            catch { }

            // 回退方案：遍历角色的 DynamicBone 组件匹配骨骼根节点
            var dbs = Character.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < dbs.Length; i++)
            {
                var b = dbs[i];
                if (b == null) continue;
                string tName = b.GetType().Name;
                if (!tName.Contains("DynamicBone") || tName.Contains("Collider")) continue;

                var rootField = b.GetType().GetField("m_Root", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (rootField != null)
                {
                    Transform rTrans = rootField.GetValue(b) as Transform;
                    if (rTrans != null && (rTrans == RootBone || rTrans == MidBone || (ParentRef != null && rTrans == ParentRef)))
                    {
                        BindDynamicBone(b);
                        break;
                    }
                }
            }
        }

        private void BindDynamicBone(MonoBehaviour db)
        {
            _dynamicBone = db;
            if (db == null) return;

            Type type = db.GetType();
            _fieldWeight = type.GetField("m_Weight", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_fieldWeight != null)
            {
                object wVal = _fieldWeight.GetValue(db);
                if (wVal is float w) _originalDbWeight = w > 0.01f ? w : 1.0f;
            }

            _fieldParticles = type.GetField("m_Particles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_fieldParticles != null)
            {
                Type listType = _fieldParticles.FieldType;
                if (listType.IsGenericType)
                {
                    Type pType = listType.GetGenericArguments()[0];
                    _fieldPos = pType.GetField("m_Position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _fieldPrevPos = pType.GetField("m_PrevPosition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _fieldParticleTransform = pType.GetField("m_Transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _dbLayoutInitialized = _fieldPos != null && _fieldPrevPos != null;
                }
            }

            _methodInitTransforms = type.GetMethod("InitTransforms", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /// <summary>
        /// 开始手部物理揉捏，锁定初始抓取状态并剥离 DynamicBone 权重
        /// </summary>
        public void StartKnead(Transform handTransform)
        {
            // 仅在静止待机状态下抓取时捕获 RestPose，避免在回弹中途抓取污染基准位姿
            if (CurrentState == State.Idle)
            {
                CacheRestPose();
            }

            CurrentState = State.Kneading;

            if (handTransform != null && ParentRef != null)
            {
                _handRestLocalPos = ParentRef.InverseTransformPoint(handTransform.position);
                _handRestLocalRot = Quaternion.Inverse(ParentRef.rotation) * handTransform.rotation;
            }

            _currentDisplacement = Vector3.zero;
            _currentRotOffset = Quaternion.identity;
            _currentUniformScale = 1.0f;
            _currentCompression = 0f;

            // 抑制原生 DynamicBone，避免与手动变形冲突
            SetDynamicBoneWeight(0f);
        }

        /// <summary>
        /// 实时揉捏形变计算：基于骨骼分层位移与轻柔旋转，坚决杜绝激进 Scale 缩放畸变
        /// </summary>
        public void UpdateKnead(Transform handTransform, out float hapticFeedbackForce)
        {
            hapticFeedbackForce = 0f;
            if (handTransform == null || ParentRef == null) return;

            Vector3 currentHandLocalPos = ParentRef.InverseTransformPoint(handTransform.position);
            Quaternion currentHandLocalRot = Quaternion.Inverse(ParentRef.rotation) * handTransform.rotation;

            // 1. 手部在父骨骼坐标系下的相对位移
            Vector3 deltaHand = currentHandLocalPos - _handRestLocalPos;

            // 2. 超弹性软限制（平滑双曲正切饱和，控制在安全位移以内，杜绝全裸网格过度拉扯拉丝）
            // 胸部安全形变上限约 8.5cm，臀部约 7.0cm
            float maxDisplacement = IsBust ? 0.085f : 0.070f;
            float rawMag = deltaHand.magnitude;
            if (rawMag > 0.0001f)
            {
                float softMag = maxDisplacement * (float)Math.Tanh(rawMag / maxDisplacement);
                _currentDisplacement = (deltaHand / rawMag) * softMag;
            }
            else
            {
                _currentDisplacement = Vector3.zero;
            }

            // 3. 计算挤压法线方向（沿骨骼朝内方向压缩，避免胸部/臀部凹陷穿模）
            Vector3 pressInwardNormal = IsBust ? new Vector3(0f, 0f, -1f) : new Vector3(0f, -1f, 0f);
            float pressDepth = Vector3.Dot(_currentDisplacement, pressInwardNormal);

            // 限制向内按压的极限深度（最大内陷不超过 2.2cm，防止穿入胸腔/骨盆产生内凹畸变）
            if (pressDepth > 0.022f)
            {
                _currentDisplacement -= pressInwardNormal * (pressDepth - 0.022f);
                pressDepth = 0.022f;
            }

            // 挤压应变度 epsilon (正值表示压缩，负值表示向外拉伸)
            const float tissueRadius = 0.080f;
            _currentCompression = Mathf.Clamp(pressDepth / tissueRadius, -0.5f, 0.6f);

            // 4. 坚决杜绝激进非等比 Scale 缩放畸变！
            // 采用严格各向同性（Isotropic）微缩放（0.985~1.015），确保矩阵无任何剪切（Zero Shear），网格绝对不发生拉丝、折角或尖角突变
            _currentUniformScale = Mathf.Clamp(1.0f + 0.015f * _currentCompression, 0.985f, 1.015f);

            // 5. 手柄相对旋转平滑软跟随
            Quaternion deltaHandRot = currentHandLocalRot * Quaternion.Inverse(_handRestLocalRot);
            _currentRotOffset = Quaternion.Slerp(Quaternion.identity, deltaHandRot, 0.5f);

            // 6. 应用变形到骨骼
            ApplyDeformationToBones(_currentRotOffset);

            // 7. 同步 DynamicBone 粒子，防止粒子拉脱或出现速度脉冲
            SynchronizeDynamicBoneParticles(Vector3.zero);

            // 8. 计算力反馈强度 (弹性势能 + 挤压阻抗)
            float strainEnergy = 0.5f * (rawMag / maxDisplacement) + 0.5f * Mathf.Abs(_currentCompression);
            hapticFeedbackForce = Mathf.Clamp01(strainEnergy);
        }

        private void ApplyDeformationToBones(Quaternion rotOffset)
        {
            if (ParentRef == null || RootBone == null) return;

            Vector3 currentScale = Vector3.one * _currentUniformScale;

            if (IsBust)
            {
                // 胸部骨骼链分层渐变位移与旋转传递：
                // A. 根骨骼 (cf_j_bust01): 承担 25% 位移，微小跟随旋转，保持乳房基底与胸壁平滑衔接
                Vector3 rootPos = _rootRestLocalPos + _currentDisplacement * 0.25f;
                Quaternion rootRot = Quaternion.Slerp(Quaternion.identity, rotOffset, 0.35f) * _rootRestLocalRot;
                RootBone.position = ParentRef.TransformPoint(rootPos);
                RootBone.rotation = ParentRef.rotation * rootRot;
                RootBone.localScale = currentScale;

                // B. 中间主受力骨骼 (cf_j_bust02): 承担 70% 位移，丰满呈现软肉摆动
                if (MidBone != null)
                {
                    Vector3 midPos = _midRestLocalPos + _currentDisplacement * 0.70f;
                    Quaternion midRot = Quaternion.Slerp(Quaternion.identity, rotOffset, 0.75f) * _midRestLocalRot;
                    MidBone.position = ParentRef.TransformPoint(midPos);
                    MidBone.rotation = ParentRef.rotation * midRot;
                    MidBone.localScale = currentScale;
                }

                // C. 尖端/乳头骨骼 (cf_j_bust03): 100% 跟随手心，顶端平滑贴合
                if (TipBone != null)
                {
                    Vector3 tipPos = _tipRestLocalPos + _currentDisplacement * 1.0f;
                    Quaternion tipRot = rotOffset * _tipRestLocalRot;
                    TipBone.position = ParentRef.TransformPoint(tipPos);
                    TipBone.rotation = ParentRef.rotation * tipRot;
                    TipBone.localScale = currentScale;
                }
            }
            else
            {
                // 臀部骨骼 (cf_j_siri): 无子骨骼链，由根骨骼直接承担柔韧位移与轻柔旋转
                Vector3 buttPos = _rootRestLocalPos + _currentDisplacement * 0.85f;
                Quaternion buttRot = Quaternion.Slerp(Quaternion.identity, rotOffset, 0.6f) * _rootRestLocalRot;
                RootBone.position = ParentRef.TransformPoint(buttPos);
                RootBone.rotation = ParentRef.rotation * buttRot;
                RootBone.localScale = currentScale;
            }
        }

        /// <summary>
        /// 松开抓握瞬间，初始化弹簧-阻尼阻抗振荡（Damped Harmonic Oscillator）回弹
        /// </summary>
        public void ReleaseKnead(Vector3 handVelocity)
        {
            // 只有处于揉捏中的部位才允许进入释放流程；目标被停用/重建后再释放会把
            // 一个已经 Idle 的部位错误地推入 Restitution，造成回弹残差或无限等待。
            if (CurrentState != State.Kneading)
                return;

            bool figurePosing = VRFigurePose.IsEnabled;
            bool dynamicTouch = VRInteractionOptions.DynamicTouchEnabled;
            if (VRStudioInteractionPolicy.BakePoseOnRelease(figurePosing))
            {
                BakeCurrentPoseAsRest();
                return;
            }
            if (!VRStudioInteractionPolicy.RestituteSoftBody(figurePosing, dynamicTouch))
            {
                CurrentState = State.Idle;
                _currentDisplacement = Vector3.zero;
                _currentRotOffset = Quaternion.identity;
                _currentUniformScale = 1.0f;
                SetDynamicBoneWeight(_originalDbWeight);
                return;
            }

            CurrentState = State.Restitution;
            _restitutionTime = 0f;

            // 约 2.4 个衰减周期：T = 2*pi/wd ≈ 0.26s, 总回弹时间约 0.62 秒，丰满 Q 弹
            _restitutionDuration = (2.4f * Mathf.PI * 2f) / DampedFrequency;

            _initialDisplacement = _currentDisplacement;
            _initialRotOffset = _currentRotOffset;
            _initialUniformScaleOffset = _currentUniformScale - 1.0f;

            // 转换手部初速度作为冲量动量传递
            if (ParentRef != null)
            {
                Vector3 localVel = ParentRef.InverseTransformVector(handVelocity);
                // 缓和手部释放速度冲量，避免甩手造成过度振荡拉伸
                _initialVelocity = Vector3.ClampMagnitude(localVel * 0.35f, 0.40f);
            }
            else
            {
                _initialVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// LateUpdate 中驱动持续揉捏形变覆盖与二阶阻尼谐振子回弹计算与 DynamicBone 渐变回归
        /// </summary>
        public void BakeCurrentPoseAsRest()
        {
            CacheRestPose();
            CurrentState = State.Idle;
            _currentDisplacement = Vector3.zero;
            _currentRotOffset = Quaternion.identity;
            _currentUniformScale = 1.0f;
            _restitutionTime = 0f;
            if (_dynamicBone != null)
                VRDynamicBoneRest.Bake(_dynamicBone);
            SetDynamicBoneWeight(_originalDbWeight);
            SynchronizeDynamicBoneParticles(Vector3.zero);
        }

        public void OnLateUpdate(float dt)
        {
            if (VRFigurePose.IsEnabled && CurrentState == State.Restitution)
            {
                BakeCurrentPoseAsRest();
                return;
            }
            if (CurrentState == State.Kneading)
            {
                // 晚于 Animator 执行，确保抓取形变稳定呈现且粒子实时跟随
                ApplyDeformationToBones(_currentRotOffset);
                SynchronizeDynamicBoneParticles(Vector3.zero);
            }
            else if (CurrentState == State.Restitution)
            {
                UpdateRestitution(dt);
            }
        }

        private void UpdateRestitution(float dt)
        {
            _restitutionTime += dt;
            float t = _restitutionTime;

            if (t >= _restitutionDuration || ParentRef == null || RootBone == null)
            {
                // 回弹完成，恢复原始形态与 DynamicBone 100% 权重接管
                FinishRestitution();
                return;
            }

            // 二阶欠阻尼谐振子精确解析解 (Underdamped Harmonic Oscillator):
            // x(t) = e^(-zeta * wn * t) * (x0 * cos(wd * t) + ((v0 + zeta * wn * x0) / wd) * sin(wd * t))
            float decay = Mathf.Exp(-DampingRatio * NaturalFrequency * t);
            float cosTerm = Mathf.Cos(DampedFrequency * t);
            float sinTerm = Mathf.Sin(DampedFrequency * t);

            Vector3 term2 = (_initialVelocity + DampingRatio * NaturalFrequency * _initialDisplacement) / DampedFrequency;
            Vector3 currentDisp = decay * (_initialDisplacement * cosTerm + term2 * sinTerm);

            // 胸壁/骨盆防穿模边界限制：在反向回弹振荡中，绝对不允许凹陷进入体内
            Vector3 pressInwardNormal = IsBust ? new Vector3(0f, 0f, -1f) : new Vector3(0f, -1f, 0f);
            float inwardComp = Vector3.Dot(currentDisp, pressInwardNormal);
            if (inwardComp > 0.015f)
            {
                currentDisp -= pressInwardNormal * (inwardComp - 0.015f);
            }

            // 旋转偏移随阻尼衰减平滑回归无畸变状态
            Quaternion currentRotOffset = Quaternion.Slerp(Quaternion.identity, _initialRotOffset, decay);

            // 微缩放比例随之同频振荡衰减，平滑回归 1.0f
            float currentScaleOffset = decay * (_initialUniformScaleOffset * cosTerm);
            _currentUniformScale = 1.0f + currentScaleOffset;

            // 瞬间速度导数，用于注入 DynamicBone 粒子速度
            Vector3 currentVel = decay * (_initialVelocity * cosTerm - (DampingRatio * NaturalFrequency * _initialVelocity + NaturalFrequency * NaturalFrequency * _initialDisplacement) / DampedFrequency * sinTerm);

            // 施加回弹骨骼位置与形变
            _currentDisplacement = currentDisp;
            ApplyDeformationToBones(currentRotOffset);

            // 同步 DynamicBone 粒子位置与初速度
            Vector3 worldVel = ParentRef.TransformVector(currentVel);
            SynchronizeDynamicBoneParticles(worldVel);

            // 在回弹周期的后半程（振幅衰减至轻微波动），使用 Hermite Smoothstep 渐变淡入 DynamicBone 权重
            float progress = t / _restitutionDuration;
            if (progress > 0.50f)
            {
                float blendFactor = Mathf.Clamp01((progress - 0.50f) / 0.50f);
                float smoothWeight = blendFactor * blendFactor * (3f - 2f * blendFactor);
                SetDynamicBoneWeight(_originalDbWeight * smoothWeight);
            }
        }

        public void FinishRestitution()
        {
            CurrentState = State.Idle;
            _currentDisplacement = Vector3.zero;
            _currentUniformScale = 1.0f;
            _currentRotOffset = Quaternion.identity;

            if (ParentRef != null && RootBone != null)
            {
                RootBone.position = ParentRef.TransformPoint(_rootRestLocalPos);
                RootBone.rotation = ParentRef.rotation * _rootRestLocalRot;

                if (MidBone != null)
                {
                    MidBone.position = ParentRef.TransformPoint(_midRestLocalPos);
                    MidBone.rotation = ParentRef.rotation * _midRestLocalRot;
                }

                if (TipBone != null)
                {
                    TipBone.position = ParentRef.TransformPoint(_tipRestLocalPos);
                    TipBone.rotation = ParentRef.rotation * _tipRestLocalRot;
                }
            }

            // 绝对、强制恢复所有骨骼 localScale 为 Vector3.one，杜绝任何形变残差！
            if (RootBone != null) RootBone.localScale = Vector3.one;
            if (MidBone != null) MidBone.localScale = Vector3.one;
            if (TipBone != null) TipBone.localScale = Vector3.one;

            // 100% 归还原生 DynamicBone 物理控制权
            SetDynamicBoneWeight(_originalDbWeight);
            SynchronizeDynamicBoneParticles(Vector3.zero);

            // 安全重置 DynamicBone 粒子状态，确保零形变残差与零爆炸
            if (_methodInitTransforms != null && _dynamicBone != null)
            {
                try
                {
                    _methodInitTransforms.Invoke(_dynamicBone, null);
                }
                catch { }
            }
        }

        private void SetDynamicBoneWeight(float weight)
        {
            if (_dynamicBone != null && _fieldWeight != null)
            {
                try
                {
                    _fieldWeight.SetValue(_dynamicBone, weight);
                }
                catch { }
            }
        }

        private void SynchronizeDynamicBoneParticles(Vector3 currentWorldVelocity)
        {
            if (!_dbLayoutInitialized || _dynamicBone == null || _fieldParticles == null) return;

            try
            {
                var list = _fieldParticles.GetValue(_dynamicBone) as IList;
                if (list == null || list.Count == 0) return;

                float dt = Time.deltaTime > 0.0001f ? Time.deltaTime : 0.016f;
                Vector3 stepBack = currentWorldVelocity * dt;

                for (int i = 0; i < list.Count; i++)
                {
                    object p = list[i];
                    if (p == null) continue;

                    Transform boneTransform = null;
                    if (_fieldParticleTransform != null)
                    {
                        boneTransform = _fieldParticleTransform.GetValue(p) as Transform;
                    }

                    if (boneTransform == null)
                    {
                        boneTransform = i == 0 ? RootBone : (i == 1 && MidBone != null ? MidBone : (TipBone != null ? TipBone : RootBone));
                    }

                    if (boneTransform != null)
                    {
                        Vector3 currentPos = boneTransform.position;
                        _fieldPos.SetValue(p, currentPos);
                        _fieldPrevPos.SetValue(p, currentPos - stepBack);
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// VR 控制器手柄交互组件（挂载于左右手），检测与软体部位的碰撞距离、捕获按键并触发受力阻尼震颤反馈
    /// </summary>
    public class VRHandKneadController : MonoBehaviour
    {
        public bool IsLeft { get; private set; }
        private SteamVR_TrackedObject _trackedObj;
        private Controller _controller;

        private KneadBodyPart _activePart;
        private Vector3 _prevHandPos;
        private Vector3 _handVelocity;
        private float _lastMicroPulseTime;

        public void Init(bool isLeft)
        {
            IsLeft = isLeft;
            _trackedObj = GetComponent<SteamVR_TrackedObject>();
            _controller = GetComponent<Controller>();
        }

        private SteamVR_Controller.Device ResolveDevice()
        {
            int deviceIndex = _trackedObj != null && _trackedObj.index != SteamVR_TrackedObject.EIndex.None
                ? (int)_trackedObj.index
                : -1;

            if (deviceIndex >= 0)
            {
                return SteamVR_Controller.Input(deviceIndex);
            }

            if (_controller != null)
            {
                return SteamVR_Controller.ForController(_controller);
            }
            return null;
        }

        private void OnDisable()
        {
            DropActivePart(Vector3.zero);
        }

        private void OnDestroy()
        {
            DropActivePart(Vector3.zero);
        }

        private void DropActivePart(Vector3 velocity)
        {
            KneadBodyPart part = _activePart;
            _activePart = null;
            if (part != null)
                part.ReleaseKnead(velocity);
        }

        private void Update()
        {
            if (VRPhysicsKneadManager.Instance == null || !VRPhysicsKneadManager.Instance.IsEnabled)
            {
                DropActivePart(Vector3.zero);
                return;
            }

            SteamVR_Controller.Device device = ResolveDevice();
            if (device == null || !device.connected)
            {
                // 手柄断连/丢失追踪时不能让部位一直停在 Kneading（DynamicBone 权重被清零）
                DropActivePart(Vector3.zero);
                return;
            }

            // 目标被停用、重载或被 Initialize 重建后，活动部位可能已被收尾为 Idle，不能继续驱动骨骼。
            if (_activePart != null && _activePart.CurrentState != KneadBodyPart.State.Kneading)
                _activePart = null;

            // 1. 计算手掌心空间位置与即时速度
            Vector3 handPos = transform.TransformPoint(0f, -0.03f, 0.055f);
            float dt = Time.deltaTime;
            if (dt > 0.0001f)
            {
                Vector3 instantVel = (handPos - _prevHandPos) / dt;
                _handVelocity = Vector3.Lerp(_handVelocity, instantVel, 0.45f);
            }
            _prevHandPos = handPos;

            // 2. 检测抓取按键输入 (Grip 抓握键 或 Trigger 扳机深入捏取)
            bool gripHeld = device.GetPress(EVRButtonId.k_EButton_Grip);
            bool gripDown = device.GetPressDown(EVRButtonId.k_EButton_Grip);

            bool triggerHeld = device.GetPress(EVRButtonId.k_EButton_Axis1)
                               || device.GetAxis(EVRButtonId.k_EButton_Axis1).x > 0.45f;
            bool triggerDown = device.GetPressDown(EVRButtonId.k_EButton_Axis1);

            bool grabActive = gripHeld || triggerHeld;
            bool grabJustStarted = gripDown || triggerDown;

            // 3. 抓取判定与状态机推进
            if (_activePart == null)
            {
                if (grabJustStarted)
                {
                    // 避开工作室正在拖拽操作轴 Gizmo 的情况
                    if (GripMoveKKCharaStudioTool.AnyObjectInteractionActive)
                        return;

                    KneadBodyPart candidate = FindNearestKneadPart(handPos, maxDistance: 0.16f);
                    if (candidate != null && candidate.CurrentState != KneadBodyPart.State.Kneading)
                    {
                        _activePart = candidate;
                        _activePart.StartKnead(transform);

                        // 捕捉初次接触轻微触感提示 (420 微秒脉冲)
                        device.TriggerHapticPulse(420, EVRButtonId.k_EButton_Axis0);
                    }
                }
            }
            else
            {
                if (!grabActive || _activePart.Character == null || !_activePart.Character.gameObject.activeInHierarchy)
                {
                    // 释放手部，触发阻尼受力回弹
                    DropActivePart(_handVelocity);
                    return;
                }

                // 处于持续揉捏中：计算挤压形变并更新手柄阻尼阻抗震动反馈
                _activePart.UpdateKnead(transform, out float feedbackForce);

                float moveSpeed = _handVelocity.magnitude;
                float intensity = VRPhysicsKneadManager.Instance.HapticIntensity;

                // 阻尼触感震动：根据形变深度提升脉冲强度；在手部动态揉搓移动时高频发出细腻脂肪微震动
                if (Time.unscaledTime - _lastMicroPulseTime >= 0.028f) // 约 35 Hz 柔和微阻抗感
                {
                    _lastMicroPulseTime = Time.unscaledTime;
                    ushort pulseDuration = (ushort)Mathf.Clamp(
                        (400f + feedbackForce * 1800f + moveSpeed * 600f) * (intensity * 2.0f),
                        250f,
                        3200f
                    );
                    device.TriggerHapticPulse(pulseDuration, EVRButtonId.k_EButton_Axis0);
                }
            }
        }

        private KneadBodyPart FindNearestKneadPart(Vector3 handPos, float maxDistance)
        {
            KneadBodyPart bestPart = null;
            float bestDistSq = maxDistance * maxDistance;

            Dictionary<ChaControl, VRKneadTarget>.ValueCollection targets = VRPhysicsKneadManager.Instance.AllTargets;
            foreach (VRKneadTarget target in targets)
            {
                if (target == null || target.Character == null || !target.Character.gameObject.activeInHierarchy)
                    continue;

                var parts = target.Parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part == null || part.Anchor == null) continue;

                    float distSq = (part.Anchor.position - handPos).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestPart = part;
                    }
                }
            }

            return bestPart;
        }
    }
}
