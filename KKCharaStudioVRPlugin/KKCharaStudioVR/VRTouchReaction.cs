using System;
using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// Muscle force feedback and physical touch reaction for characters in VR Studio.
    /// Injects animation curve-driven displacement offsets into RootMotion FinalIK effectors
    /// when the character's body (shoulders, chest, pelvis, thighs, limbs) is slapped or pushed,
    /// producing natural elastic body deformation, backward leaning, and realistic recoil rebound.
    /// </summary>
    public class VRTouchReaction : MonoBehaviour
    {
        private class HitPoint
        {
            internal readonly int id;
            private readonly IKEffector _effector;
            internal float weight;
            private readonly bool _useUp;
            private AnimationCurve _curveForce;
            private AnimationCurve _curveUp;
            private Vector3 _vecForce;
            private Vector3 _vecUp;
            private Vector3 _offset;

            internal HitPoint(IKEffector effector, int index, float weight, bool useUp = true)
            {
                this.id = index;
                this._effector = effector;
                this.weight = weight;
                this._useUp = useUp;
            }

            internal void Start(Vector3 vecForce, Vector3 vecUp, AnimationCurve curveForce, AnimationCurve curveUp)
            {
                _offset = Vector3.zero;
                _vecForce = vecForce;
                _vecUp = vecUp;
                _curveForce = curveForce;
                _curveUp = curveUp;
            }

            internal void Override(Vector3 vecForce, Vector3 vecUp, AnimationCurve curveForce, AnimationCurve curveUp)
            {
                float totalMag = (vecForce + _offset).magnitude;
                float currentRatio = totalMag > 0.0001f ? _offset.magnitude / totalMag : 0f;
                curveForce.MoveKey(0, new Keyframe(0f, currentRatio));
                _vecForce = _offset + vecForce;
                _vecUp = vecUp;
                _curveForce = curveForce;
                _curveUp = curveUp;
            }

            internal void Move(float time)
            {
                if (weight != 0f && _effector != null && _curveForce != null)
                {
                    _offset = _curveForce.Evaluate(time) * _vecForce;
                    if (_useUp && _curveUp != null)
                    {
                        _offset += _curveUp.Evaluate(time) * _vecUp;
                    }
                    _effector.positionOffset += _offset * weight;
                }
            }
        }

        private readonly Dictionary<List<HitPoint>, float[]> _currentReactions = new Dictionary<List<HitPoint>, float[]>();
        private readonly List<List<HitPoint>> _reactionList = new List<List<HitPoint>>();
        private readonly List<List<HitPoint>> _finishedReactions = new List<List<HitPoint>>();

        private IKEffector[] _effectors;
        private FullBodyBipedIK _fbik;
        private ChaControl _chara;
        private bool _isInitialized;

        public bool IsBusy => _currentReactions.Count > 0;

        public static VRTouchReaction GetOrAttach(ChaControl chara)
        {
            if (chara == null) return null;

            GameObject target = chara.objAnim != null ? chara.objAnim : chara.gameObject;
            var reaction = target.GetComponent<VRTouchReaction>();
            if (reaction == null)
            {
                reaction = target.AddComponent<VRTouchReaction>();
            }
            return reaction;
        }

        private void Awake()
        {
            _chara = GetComponentInParent<ChaControl>() ?? GetComponent<ChaControl>();
            TryInitialize();
        }

        private void Start()
        {
            if (!_isInitialized)
            {
                TryInitialize();
            }
        }

        public bool TryInitialize()
        {
            if (_isInitialized) return true;

            if (_chara == null)
            {
                _chara = GetComponentInParent<ChaControl>() ?? GetComponent<ChaControl>();
            }

            // Find FullBodyBipedIK component
            if (_fbik == null)
            {
                _fbik = GetComponent<FullBodyBipedIK>();
                if (_fbik == null && _chara != null && _chara.objAnim != null)
                {
                    _fbik = _chara.objAnim.GetComponent<FullBodyBipedIK>();
                }
                if (_fbik == null && _chara != null)
                {
                    _fbik = _chara.GetComponentInChildren<FullBodyBipedIK>(true);
                }
                if (_fbik == null && Studio.Studio.Instance != null && Studio.Studio.Instance.dicObjectCtrl != null && _chara != null)
                {
                    foreach (var kvp in Studio.Studio.Instance.dicObjectCtrl)
                    {
                        if (kvp.Value is Studio.OCIChar ociChar && ociChar.charInfo == _chara)
                        {
                            _fbik = ociChar.finalIK;
                            break;
                        }
                    }
                }
            }

            if (_fbik == null || _fbik.solver == null || _fbik.solver.effectors == null)
            {
                return false;
            }

            _effectors = _fbik.solver.effectors;
            if (_effectors.Length < 9)
            {
                return false;
            }

            BuildReactionList();
            _isInitialized = true;
            return true;
        }

        private void BuildReactionList()
        {
            _reactionList.Clear();
            var eff = _effectors;

            // 0: LowerBody / Spine
            _reactionList.Add(new List<HitPoint>(8)
            {
                new HitPoint(eff[0], 0, 0.15f),
                new HitPoint(eff[1], 1, 0.05f),
                new HitPoint(eff[2], 2, 0.05f),
                new HitPoint(eff[3], 3, 0f),
                new HitPoint(eff[4], 4, 0f),
                new HitPoint(eff[5], 5, -0.1f),
                new HitPoint(eff[6], 6, -0.1f),
                new HitPoint(eff[7], 7, 0.5f, useUp: false)
            });

            // 1: MuneL / ArmL / Left Shoulder
            _reactionList.Add(new List<HitPoint>(6)
            {
                new HitPoint(eff[1], 1, 0.1f),
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[2], 2, -0.05f),
                new HitPoint(eff[3], 3, 0.05f),
                new HitPoint(eff[4], 4, -0.05f),
                new HitPoint(eff[5], 5, 0.05f)
            });

            // 2: MuneR / ArmR / Right Shoulder
            _reactionList.Add(new List<HitPoint>(6)
            {
                new HitPoint(eff[2], 2, 0.1f),
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[1], 1, -0.05f),
                new HitPoint(eff[3], 3, -0.05f),
                new HitPoint(eff[4], 4, 0.05f),
                new HitPoint(eff[6], 6, 0.05f)
            });

            // 3: ThighL
            _reactionList.Add(new List<HitPoint>(6)
            {
                new HitPoint(eff[3], 3, 0.1f),
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[1], 1, 0f),
                new HitPoint(eff[2], 2, 0f),
                new HitPoint(eff[4], 4, 0.05f),
                new HitPoint(eff[7], 7, 0.1f)
            });

            // 4: ThighR
            _reactionList.Add(new List<HitPoint>(4)
            {
                new HitPoint(eff[0], 0, 0.2f),
                new HitPoint(eff[3], 3, 0.05f),
                new HitPoint(eff[4], 4, 0.15f),
                new HitPoint(eff[8], 8, 0.1f)
            });

            // 5: HandL / ForearmL
            _reactionList.Add(new List<HitPoint>(6)
            {
                new HitPoint(eff[5], 5, 0.1f),
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[1], 1, 0.05f),
                new HitPoint(eff[2], 2, -0.05f),
                new HitPoint(eff[3], 3, 0.05f, useUp: false),
                new HitPoint(eff[4], 4, -0.05f, useUp: false)
            });

            // 6: HandR / ForearmR
            _reactionList.Add(new List<HitPoint>(6)
            {
                new HitPoint(eff[6], 6, 0.1f),
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[1], 1, -0.05f),
                new HitPoint(eff[2], 2, 0.05f),
                new HitPoint(eff[3], 3, -0.05f, useUp: false),
                new HitPoint(eff[4], 4, 0.05f, useUp: false)
            });

            // 7: FootL
            _reactionList.Add(new List<HitPoint>(5)
            {
                new HitPoint(eff[1], 1, -0.05f),
                new HitPoint(eff[2], 2, -0.05f),
                new HitPoint(eff[3], 3, 0.05f),
                new HitPoint(eff[4], 4, 0.05f),
                new HitPoint(eff[0], 0, 0.1f)
            });

            // 8: FootR
            _reactionList.Add(new List<HitPoint>(5)
            {
                new HitPoint(eff[1], 7, -0.05f),
                new HitPoint(eff[2], 8, -0.05f),
                new HitPoint(eff[3], 3, 0.05f),
                new HitPoint(eff[4], 4, 0.05f),
                new HitPoint(eff[0], 0, 0.1f)
            });

            // 9: Head / UpperBody (Chest, Shoulders, Spine) - leaning back & recoil
            _reactionList.Add(new List<HitPoint>(9)
            {
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[1], 1, 0.15f),
                new HitPoint(eff[2], 2, 0.15f),
                new HitPoint(eff[3], 3, 0.1f, useUp: false),
                new HitPoint(eff[4], 4, 0.1f, useUp: false),
                new HitPoint(eff[5], 5, 0.05f),
                new HitPoint(eff[6], 6, 0f),
                new HitPoint(eff[7], 7, 0.4f, useUp: false),
                new HitPoint(eff[8], 8, 0f, useUp: false)
            });

            // 10: Asoko / Groin / Buttocks
            _reactionList.Add(new List<HitPoint>(9)
            {
                new HitPoint(eff[3], 3, 0.05f, useUp: false),
                new HitPoint(eff[4], 4, 0f, useUp: false),
                new HitPoint(eff[0], 0, 0.1f, useUp: false),
                new HitPoint(eff[1], 1, 0.05f, useUp: false),
                new HitPoint(eff[2], 2, -0.05f),
                new HitPoint(eff[5], 5, 0.05f),
                new HitPoint(eff[6], 6, 0.05f),
                new HitPoint(eff[7], 7, 0.05f, useUp: false),
                new HitPoint(eff[8], 8, 0f)
            });

            // 11: LegL
            _reactionList.Add(new List<HitPoint>(3)
            {
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[3], 3, 0.1f),
                new HitPoint(eff[7], 7, 0.2f)
            });

            // 12: LegR
            _reactionList.Add(new List<HitPoint>(3)
            {
                new HitPoint(eff[0], 0, 0.05f),
                new HitPoint(eff[3], 3, 0.1f),
                new HitPoint(eff[8], 8, 0.2f)
            });
        }

        private Vector3 GetUpVec(int zeroIndexMasterId)
        {
            if (_effectors == null || _effectors.Length == 0) return Vector3.up;

            if ((uint)(zeroIndexMasterId - 5) <= 3u && zeroIndexMasterId < _effectors.Length)
            {
                int parentIdx = zeroIndexMasterId - 4;
                if (parentIdx >= 0 && parentIdx < _effectors.Length)
                {
                    Transform t1 = _effectors[parentIdx].bone;
                    Transform t2 = _effectors[zeroIndexMasterId].bone;
                    if (t1 != null && t2 != null)
                    {
                        return (t1.position - t2.position).normalized;
                    }
                }
            }

            if (_effectors[0] != null && _effectors[0].bone != null)
            {
                return _effectors[0].bone.up;
            }
            return Vector3.up;
        }

        private Vector3 HelpVector(int index, Vector3 direction, Vector3 upVec)
        {
            if (_effectors == null || index >= _effectors.Length) return direction;

            if (index == 5 || index == 6)
            {
                Transform bone = _effectors[index].bone;
                int parentIdx = index - 4;
                if (parentIdx >= 0 && parentIdx < _effectors.Length && bone != null)
                {
                    Transform bone2 = _effectors[parentIdx].bone;
                    if (bone2 != null)
                    {
                        if ((index == 5 && bone.InverseTransformPoint(bone2.position).y > 0.2f) ||
                            (index == 6 && bone.InverseTransformPoint(bone2.position).y < -0.2f))
                        {
                            return Quaternion.Euler(0f, 180f, 0f) * direction * 0.5f + upVec * UnityEngine.Random.Range(0.6f, 1f);
                        }
                    }
                }

                if (_effectors[0] != null && _effectors[0].bone != null && bone != null)
                {
                    Vector3 from = _effectors[0].bone.position - bone.position;
                    from.y = 0f;
                    if (Vector3.Angle(from, direction) < 45f)
                    {
                        float angle = UnityEngine.Random.Range(45f, 90f) * (UnityEngine.Random.value > 0.5f ? 1f : -1f);
                        return Quaternion.Euler(0f, angle, 0f) * direction;
                    }
                }
            }
            return direction;
        }

        public void React(int id, Vector3 direction, float intensity = 1f)
        {
            if (!_isInitialized && !TryInitialize())
            {
                return;
            }

            if (id < 0 || id >= _reactionList.Count)
            {
                id = 0;
            }

            List<HitPoint> list = _reactionList[id];
            if (list == null || list.Count == 0) return;

            Vector3 upVec = GetUpVec(list[0].id);
            direction = HelpVector(list[0].id, direction, upVec);

            // Scale displacement force by touch intensity (slap speed or push force)
            float forceScale = 0.08f * Mathf.Clamp(intensity, 0.4f, 2.5f);
            Vector3 scaledForce = direction.normalized * forceScale;

            if (id == 9 && _effectors != null && _effectors[0] != null && _effectors[0].bone != null)
            {
                bool flag = Vector3.SignedAngle(direction, _effectors[0].bone.forward, _effectors[0].bone.up) > 0f;
                list[5].weight = flag ? 0f : 0.05f;
                list[6].weight = flag ? 0.05f : 0f;
                list[7].weight = flag ? 0f : UnityEngine.Random.Range(0.3f, 0.5f);
                list[8].weight = flag ? UnityEngine.Random.Range(0.3f, 0.5f) : 0f;
            }

            float maxDuration = 0f;
            if (!_currentReactions.ContainsKey(list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    AnimationCurve forceCurve = GetForceCurve(out float dur);
                    AnimationCurve upCurve = GetUpCurve(dur);
                    list[i].Start(scaledForce, upVec, forceCurve, upCurve);
                    maxDuration = Mathf.Max(maxDuration, dur);
                }
                _currentReactions.Add(list, new float[2] { 0f, maxDuration });
            }
            else
            {
                for (int j = 0; j < list.Count; j++)
                {
                    AnimationCurve forceCurve = GetForceCurve(out float dur);
                    AnimationCurve upCurve = GetUpCurve(dur);
                    list[j].Override(scaledForce, upVec, forceCurve, upCurve);
                    maxDuration = Mathf.Max(maxDuration, dur);
                }
                _currentReactions[list][0] = 0f;
                _currentReactions[list][1] = maxDuration;
            }
        }

        private void LateUpdate()
        {
            if (_currentReactions.Count <= 0) return;

            _finishedReactions.Clear();
            foreach (var kvp in _currentReactions)
            {
                float[] timing = kvp.Value;
                if (timing[0] > timing[1])
                {
                    _finishedReactions.Add(kvp.Key);
                    continue;
                }

                timing[0] += Time.deltaTime;
                float elapsed = timing[0];
                foreach (HitPoint hp in kvp.Key)
                {
                    hp.Move(elapsed);
                }
            }

            for (int i = 0; i < _finishedReactions.Count; i++)
            {
                _currentReactions.Remove(_finishedReactions[i]);
            }
        }

        private AnimationCurve GetForceCurve(out float duration)
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(UnityEngine.Random.Range(0.15f, 0.35f), UnityEngine.Random.Range(0.85f, 1.1f)),
                new Keyframe(UnityEngine.Random.Range(0.55f, 0.95f), UnityEngine.Random.Range(0.4f, 0.7f)),
                new Keyframe(duration = UnityEngine.Random.Range(1.2f, 2.0f), 0f)
            );
        }

        private AnimationCurve GetUpCurve(float duration)
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(UnityEngine.Random.Range(0.15f, 0.4f), UnityEngine.Random.Range(0.5f, 0.9f)),
                new Keyframe(UnityEngine.Random.Range(0.6f, 1.0f), UnityEngine.Random.Range(0.2f, 0.5f)),
                new Keyframe(duration, 0f)
            );
        }
    }
}
