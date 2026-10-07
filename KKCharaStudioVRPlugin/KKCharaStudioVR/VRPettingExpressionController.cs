using System;
using System.Collections;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// Character facial micro-expression controller for VR petting and slapping in Studio VR.
    /// Drives:
    /// - Petting mode (Caress):
    ///   - Blushing (hohoAkaRate smoothly rises by +0.2~0.4);
    ///   - Gentle squinting / soft blinking (eyesOpenMax eases to 0.4~0.6 in pleasure);
    ///   - Gaze tilt / shy averted look (eyesLookPtn 3 / shy glance away).
    /// - Slap mode (Slap/Boop):
    ///   - Sudden startled wide-eyes, followed by rapid blink;
    ///   - Startled gasp mouth;
    ///   - Sudden blush burst;
    ///   - Smooth recovery back to baseline pose after contact ceases.
    /// </summary>
    public class VRPettingExpressionController : MonoBehaviour
    {
        private ChaControl _chara;

        // Baseline expression preservation
        private float _baseBlush;
        private float _baseEyesOpenMax;
        private int _baseEyesPtn;
        private int _baseMouthPtn;
        private int _baseLookPtn;
        private bool _baselineRecorded;

        // Dynamic expression states
        private bool _isPetting;
        private float _pettingEndTime;
        private float _targetBlush;
        private float _currentBlush;
        private float _targetEyesOpen;
        private float _currentEyesOpen;
        private bool _isSlapReacting;
        private float _nextBlinkTime;
        private bool _isSoftBlinking;

        public static VRPettingExpressionController GetOrAttach(ChaControl chara)
        {
            if (chara == null) return null;
            GameObject target = chara.objAnim != null ? chara.objAnim : chara.gameObject;
            var controller = target.GetComponent<VRPettingExpressionController>();
            if (controller == null)
            {
                controller = target.AddComponent<VRPettingExpressionController>();
                controller._chara = chara;
            }
            return controller;
        }

        private void Awake()
        {
            if (_chara == null)
            {
                _chara = GetComponentInParent<ChaControl>() ?? GetComponent<ChaControl>();
            }
        }

        private void Start()
        {
            RecordBaseline();
            _nextBlinkTime = Time.unscaledTime + UnityEngine.Random.Range(2.0f, 4.0f);
        }

        public void RecordBaseline()
        {
            if (_chara == null || _chara.fileStatus == null) return;
            _baseBlush = _chara.fileStatus.hohoAkaRate;
            _baseEyesOpenMax = _chara.fileStatus.eyesOpenMax;
            _baseEyesPtn = _chara.fileStatus.eyesPtn;
            _baseMouthPtn = _chara.fileStatus.mouthPtn;
            _baseLookPtn = _chara.fileStatus.eyesLookPtn;
            _currentBlush = _baseBlush;
            _currentEyesOpen = _baseEyesOpenMax;
            _baselineRecorded = true;
        }

        /// <summary>
        /// Called continuously when hand is gently caressing/petting character colliders.
        /// </summary>
        public void OnPettingStay(VRPhysicalTouchHandler.BodyPartType part, float speed)
        {
            if (_chara == null || _isSlapReacting) return;
            if (!_baselineRecorded) RecordBaseline();

            _isPetting = true;
            _pettingEndTime = Time.unscaledTime + 1.25f;

            // 1. Blushing: sensitive body parts trigger more blush
            float blushBoost = 0.30f;
            if (part == VRPhysicalTouchHandler.BodyPartType.Asoko ||
                part == VRPhysicalTouchHandler.BodyPartType.MuneL ||
                part == VRPhysicalTouchHandler.BodyPartType.MuneR ||
                part == VRPhysicalTouchHandler.BodyPartType.Groin)
            {
                blushBoost = 0.45f;
            }
            else if (part == VRPhysicalTouchHandler.BodyPartType.Head)
            {
                blushBoost = 0.25f;
            }

            _targetBlush = Mathf.Clamp01(_baseBlush + blushBoost);

            // 2. Eye squinting: comfortable pleasant squint (0.45 ~ 0.60 open)
            _targetEyesOpen = Mathf.Clamp(_baseEyesOpenMax * 0.55f, 0.35f, 0.65f);

            // 3. Shy gaze tilt: occasionally avert gaze during sensitive caressing
            if (part == VRPhysicalTouchHandler.BodyPartType.Head ||
                part == VRPhysicalTouchHandler.BodyPartType.MuneL ||
                part == VRPhysicalTouchHandler.BodyPartType.MuneR)
            {
                if (_chara.fileStatus.eyesLookPtn != 3) // 3 = Me wo sorasu / Avert gaze
                {
                    _chara.ChangeLookEyesPtn(3);
                }
            }
        }

        /// <summary>
        /// Called when character is hit with a high-speed slap (>= 0.8 m/s).
        /// </summary>
        public void OnSlap(VRPhysicalTouchHandler.BodyPartType part, float speed)
        {
            if (_chara == null) return;
            if (!_baselineRecorded) RecordBaseline();

            if (_isSlapReacting)
            {
                StopAllCoroutines();
            }

            StartCoroutine(Co_SlapReaction(speed));
        }

        private IEnumerator Co_SlapReaction(float speed)
        {
            _isSlapReacting = true;
            _isPetting = false;

            // 1. Instant startled reaction: eyes wide open, sudden gasp mouth
            _chara.ChangeEyesOpenMax(1.0f);
            _chara.ChangeMouthPtn(1); // open mouth / gasp
            float slapBlush = Mathf.Clamp01(_baseBlush + 0.40f);
            _chara.ChangeHohoAkaRate(slapBlush);

            // 2. After 0.15s, hard startled blink
            yield return new WaitForSeconds(0.15f);
            _chara.ChangeEyesOpenMax(0.0f); // tight blink

            yield return new WaitForSeconds(0.18f);
            _chara.ChangeEyesOpenMax(0.85f);

            // 3. Gradual ease back to baseline over 1.8 seconds
            float elapsed = 0f;
            float duration = 1.8f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                float currBlush = Mathf.Lerp(slapBlush, _baseBlush, t);
                _chara.ChangeHohoAkaRate(currBlush);

                float currEyes = Mathf.Lerp(0.85f, _baseEyesOpenMax, t);
                _chara.ChangeEyesOpenMax(currEyes);

                yield return null;
            }

            // Restore baseline mouth and look
            _chara.ChangeMouthPtn(_baseMouthPtn);
            _chara.ChangeLookEyesPtn(_baseLookPtn);
            _chara.ChangeHohoAkaRate(_baseBlush);
            _chara.ChangeEyesOpenMax(_baseEyesOpenMax);

            _currentBlush = _baseBlush;
            _currentEyesOpen = _baseEyesOpenMax;
            _isSlapReacting = false;
        }

        private void Update()
        {
            if (_chara == null || _chara.fileStatus == null || _isSlapReacting) return;

            // Check petting timeout
            if (_isPetting && Time.unscaledTime > _pettingEndTime)
            {
                _isPetting = false;
                _targetBlush = _baseBlush;
                _targetEyesOpen = _baseEyesOpenMax;

                // Restore gaze pattern
                if (_chara.fileStatus.eyesLookPtn != _baseLookPtn)
                {
                    _chara.ChangeLookEyesPtn(_baseLookPtn);
                }
            }

            // Smooth interpolation for blushing
            if (Mathf.Abs(_currentBlush - _targetBlush) > 0.005f)
            {
                float speed = _isPetting ? 0.75f : 0.45f;
                _currentBlush = Mathf.MoveTowards(_currentBlush, _targetBlush, Time.deltaTime * speed);
                _chara.ChangeHohoAkaRate(_currentBlush);
            }

            // Periodic soft blinking during continuous petting
            if (_isPetting && !_isSoftBlinking && Time.unscaledTime >= _nextBlinkTime)
            {
                StartCoroutine(Co_SoftBlink());
                _nextBlinkTime = Time.unscaledTime + UnityEngine.Random.Range(2.5f, 4.0f);
            }

            // Smooth interpolation for eyelid aperture
            if (!_isSoftBlinking && Mathf.Abs(_currentEyesOpen - _targetEyesOpen) > 0.005f)
            {
                float speed = _isPetting ? 1.2f : 0.8f;
                _currentEyesOpen = Mathf.MoveTowards(_currentEyesOpen, _targetEyesOpen, Time.deltaTime * speed);
                _chara.ChangeEyesOpenMax(_currentEyesOpen);
            }
        }

        private IEnumerator Co_SoftBlink()
        {
            _isSoftBlinking = true;
            float startOpen = _currentEyesOpen;

            // Close eyelid
            float elapsed = 0f;
            float closeDuration = 0.08f;
            while (elapsed < closeDuration)
            {
                elapsed += Time.deltaTime;
                float val = Mathf.Lerp(startOpen, 0.05f, elapsed / closeDuration);
                _chara.ChangeEyesOpenMax(val);
                yield return null;
            }

            // Hold shut briefly
            yield return new WaitForSeconds(0.04f);

            // Re-open to squint target
            elapsed = 0f;
            float openDuration = 0.12f;
            while (elapsed < openDuration)
            {
                elapsed += Time.deltaTime;
                float val = Mathf.Lerp(0.05f, _targetEyesOpen, elapsed / openDuration);
                _chara.ChangeEyesOpenMax(val);
                yield return null;
            }

            _currentEyesOpen = _targetEyesOpen;
            _isSoftBlinking = false;
        }

        private void OnDisable()
        {
            if (_chara != null && _baselineRecorded)
            {
                _chara.ChangeHohoAkaRate(_baseBlush);
                _chara.ChangeEyesOpenMax(_baseEyesOpenMax);
                _chara.ChangeLookEyesPtn(_baseLookPtn);
                _chara.ChangeMouthPtn(_baseMouthPtn);
            }
        }
    }
}
