using UnityEngine;
using VRGIN.Core;
using Valve.VR;

namespace KKCharaStudioVR
{
    public class VRTwoHandScale : MonoBehaviour
    {
        public static VRTwoHandScale Instance { get; private set; }

        public bool IsScaling
        {
            get { return _isScaling; }
        }

        public bool ShouldSuppressWorldMove
        {
            get
            {
                if (!VRMmdPlaybackController.MovementPlan.AllowWorldGrab)
                    return false;
                return !VRWristMenuController.IsOpen
                    && isActiveAndEnabled
                    && IsFeatureEnabled
                    && AreBothGripsPressed();
            }
        }

        private float _initialDistance;
        private Vector3 _initialScale;
        private bool _isScaling;
        private bool _hasUserScale;
        private float _userScale = 1f;
        private float _smoothedScale = 1f;
        private Vector3 _smoothedMidpoint;
        private bool _scaleFilterReady;
        private KKCharaStudioVRSettings _settings;

        private bool IsFeatureEnabled
        {
            get { return _settings == null || _settings.TwoHandScaleEnabled; }
        }

        internal static void CancelTimelineInteraction()
        {
            if (Instance != null)
                Instance.ResetScaling();
        }

        void Start()
        {
            Instance = this;
            if (VR.Manager != null && VR.Manager.Context != null)
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
        }

        void OnLevelWasLoaded(int level)
        {
            LocomotionInputGate.DisarmStuckGates();
        }

        void Update()
        {
            // A lock that has been true since startup (no Timeline/MMD actually
            // playing) must not disable scaling for the whole session.
            if (!IsFeatureEnabled || VRWristMenuController.IsOpen
                || !VRMmdPlaybackController.MovementPlan.AllowWorldGrab
                || LocomotionInputGate.BlockUnlessStuck("IsManualMovementLocked", VRTimelineCameraFollowController.IsManualMovementLocked))
            {
                ResetScaling();
                return;
            }

            SteamVR_Controller.Device leftDevice;
            SteamVR_Controller.Device rightDevice;
            if (!TryGetControllers(out leftDevice, out rightDevice))
            {
                ResetScaling();
                return;
            }

            bool bothGrip = leftDevice.GetPress(EVRButtonId.k_EButton_Grip)
                         && rightDevice.GetPress(EVRButtonId.k_EButton_Grip);
            if (!bothGrip || GripMoveKKCharaStudioTool.AnyObjectInteractionActive)
            {
                ResetScaling();
                return;
            }

            Transform origin = GripMoveKKCharaStudioTool.PlayerOrigin();
            ReapplyUserScale(origin);
            if (origin == null || Mathf.Abs(origin.localScale.x) < 0.0001f)
            {
                ResetScaling();
                return;
            }

            Vector3 leftPos = ((Component)VR.Mode.Left).transform.position;
            Vector3 rightPos = ((Component)VR.Mode.Right).transform.position;
            float worldDist = Vector3.Distance(leftPos, rightPos);
            float unscaledDist = worldDist / Mathf.Abs(origin.localScale.x);

            if (!_isScaling)
            {
                if (unscaledDist <= 0.01f) return;
                _isScaling = true;
                _initialDistance = unscaledDist;
                _initialScale = origin.localScale;
                _smoothedScale = Mathf.Abs(_initialScale.x);
                _smoothedMidpoint = (leftPos + rightPos) * 0.5f;
                _scaleFilterReady = true;
                VRLog.Info("Two-hand scale begin");
                leftDevice.TriggerHapticPulse(500, EVRButtonId.k_EButton_Axis0);
                rightDevice.TriggerHapticPulse(500, EVRButtonId.k_EButton_Axis0);
                return;
            }

            if (_initialDistance <= 0.01f || !_scaleFilterReady) return;

            float ratio = unscaledDist / _initialDistance;
            float targetMagnitude = Mathf.Clamp(Mathf.Abs(_initialScale.x) * ratio, 0.1f, 10f);
            float alpha = VRMotionFilter.Alpha(16f);
            float blended = Mathf.Lerp(_smoothedScale, targetMagnitude, alpha);
            float maxStep = Mathf.Max(0.2f, _smoothedScale) * 8f * VRMotionFilter.DeltaTime();
            _smoothedScale = Mathf.MoveTowards(_smoothedScale, blended, maxStep);
            Vector3 midpoint = (leftPos + rightPos) * 0.5f;
            _smoothedMidpoint = Vector3.Lerp(_smoothedMidpoint, midpoint, alpha);

            Vector3 originToMid = _smoothedMidpoint - origin.position;
            float currentMagnitude = Mathf.Abs(origin.localScale.x);
            if (currentMagnitude < 0.0001f) return;
            float scaleChange = _smoothedScale / currentMagnitude;

            _userScale = _smoothedScale;
            _hasUserScale = true;
            origin.localScale = Vector3.one * _smoothedScale;
            Vector3 newPos = _smoothedMidpoint - originToMid * scaleChange;
            newPos.y = origin.position.y;
            origin.position = newPos;
        }

        private bool TryGetControllers(out SteamVR_Controller.Device leftDevice, out SteamVR_Controller.Device rightDevice)
        {
            leftDevice = null;
            rightDevice = null;
            if (VR.Mode == null || VR.Mode.Left == null || VR.Mode.Right == null) return false;

#if KKS
            // Grip state does not need a published device index or a valid pose.
            leftDevice = SteamVR_Controller.ForController(VR.Mode.Left);
            rightDevice = SteamVR_Controller.ForController(VR.Mode.Right);
            return leftDevice != null && leftDevice.connected
                && rightDevice != null && rightDevice.connected;
#else
            SteamVR_TrackedObject leftTracked = ((Component)VR.Mode.Left).GetComponent<SteamVR_TrackedObject>();
            SteamVR_TrackedObject rightTracked = ((Component)VR.Mode.Right).GetComponent<SteamVR_TrackedObject>();
            if (leftTracked == null || rightTracked == null) return false;
            if (leftTracked.index == SteamVR_TrackedObject.EIndex.None ||
                rightTracked.index == SteamVR_TrackedObject.EIndex.None) return false;

            leftDevice = SteamVR_Controller.Input((int)leftTracked.index);
            rightDevice = SteamVR_Controller.Input((int)rightTracked.index);
            return leftDevice != null && rightDevice != null;
#endif
        }

        private bool AreBothGripsPressed()
        {
            SteamVR_Controller.Device leftDevice;
            SteamVR_Controller.Device rightDevice;
            return TryGetControllers(out leftDevice, out rightDevice)
                && leftDevice.GetPress(EVRButtonId.k_EButton_Grip)
                && rightDevice.GetPress(EVRButtonId.k_EButton_Grip);
        }

        private void ResetScaling()
        {
            if (_isScaling)
                VRLog.Info("Two-hand scale end");
            _isScaling = false;
            _scaleFilterReady = false;
            _initialDistance = 0f;
        }

        // VRGIN_OpenXR VRCamera.OnUpdate writes origin.localScale = IPDScale
        // every Update, which would undo a completed two-hand scale before render.
        private void ReapplyUserScale(Transform origin)
        {
            if (!_hasUserScale || origin == null)
                return;
            if (Mathf.Abs(origin.localScale.x - _userScale) > 0.0001f)
                origin.localScale = Vector3.one * _userScale;
        }

        void LateUpdate()
        {
            if (_hasUserScale)
                ReapplyUserScale(GripMoveKKCharaStudioTool.PlayerOrigin());
        }

        void OnDisable()
        {
            ResetScaling();
        }

        void OnDestroy()
        {
            ResetScaling();
            if (Instance == this) Instance = null;
        }
    }
}
