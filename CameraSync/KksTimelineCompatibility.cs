#if KKS
using UnityEngine;

namespace KK_VR_CameraSync
{
    // KKS port of the wrist-menu contract. Rebuild from one camera-local
    // anchor each frame so user offsets and physical tracking never accumulate.
    internal sealed partial class CameraSyncDriver
    {
        private bool _timelineSuppressed, _externalOwner, _hasFovOverride, _fovOverrideEnabled;
        private float _fovOverride = 53.13f, _verticalOffset, _yawOffset;
        private bool _timelineAnchorValid;
        private Vector3 _timelineLocalPosition;
        private Quaternion _timelineLocalRotation;
        private CameraPoseSource _timelineAnchorSource;
        private Transform _timelineOrigin;
        private float _appliedVerticalOffset, _appliedYawOffset;

        internal void SetTimelineCameraSuppressed(bool value)
        {
            if (_timelineSuppressed == value) return;
            _timelineSuppressed = value;
            ResetBaseline();
        }
        internal void SetExternalVrCameraOwner(bool value)
        {
            if (_externalOwner == value) return;
            _externalOwner = value;
            ResetBaseline();
        }
        internal void SetTimelineFovCompensationOverride(bool enabled, float reference)
        {
            if (float.IsNaN(reference) || float.IsInfinity(reference)) return;
            _hasFovOverride = true;
            _fovOverrideEnabled = enabled;
            _fovOverride = Mathf.Clamp(reference, 20, 120);
        }
        internal void ClearTimelineFovCompensationOverride() { _hasFovOverride = false; }
        internal void SetTimelineUserPoseOffset(float vertical, float yaw)
        {
            if (float.IsNaN(vertical) || float.IsInfinity(vertical) || float.IsNaN(yaw) || float.IsInfinity(yaw)) return;
            _verticalOffset = Mathf.Clamp(vertical, -10, 10);
            _yawOffset = Mathf.DeltaAngle(0, yaw);
        }
        internal void ClearTimelineUserPoseOffset() { _verticalOffset = _yawOffset = 0; }
        internal void ResumeAndPrimeTimelineState() { ResumeAndReset(); }
        internal bool IsTimelineCameraWriterActive()
        {
            bool playing;
            Transform origin, head;
            return Plugin.Instance != null && Plugin.Instance.SyncEnabled.Value && !IsSuspended && !IsSceneLoading()
                && !_timelineSuppressed && !_externalOwner && TryGetVrRig(out origin, out head)
                && TryGetTimelinePlaybackState(out playing) && playing;
        }

        private bool ApplyKksTimeline(Transform origin, Transform head, bool playing)
        {
            if (_externalOwner || (playing && _timelineSuppressed))
            {
                _timelineAnchorValid = _baselineValid = false;
                return true;
            }
            if (!playing)
            {
                if (_timelineAnchorValid) _baselineValid = false;
                _timelineAnchorValid = false;
                return false;
            }
            Plugin plugin = Plugin.Instance;
            CameraPose target;
            bool compensate = _hasFovOverride ? _fovOverrideEnabled : plugin.CompensateTimelineFov.Value;
            float fov = _hasFovOverride ? _fovOverride : plugin.TimelineReferenceFov.Value;
            if (!TryGetStudioCameraPose(compensate, fov, out target))
            {
                _timelineAnchorValid = _baselineValid = false;
                return true;
            }
            CameraRotationMode mode = plugin.TimelineFullRotation.Value ? CameraRotationMode.Full : plugin.RotationMode.Value;
            target.Rotation = FilterRotation(target.Rotation, mode);
            if (!_timelineAnchorValid || _timelineAnchorSource != target.Source || _timelineOrigin != origin)
            {
                if (plugin.AlignOnTimelinePlay.Value)
                {
                    SnapHeadToTarget(origin, head, target.Position, target.Rotation, mode);
                    CaptureTimelineAnchor(target.Position, target.Rotation, origin.position, origin.rotation, 0, 0);
                }
                else
                {
                    // The current origin may already include wrist-menu offsets
                    // from the previous play session. Remove them when capturing
                    // the anchor so resume/source changes do not apply them twice.
                    CaptureTimelineAnchor(target.Position, target.Rotation, origin.position, origin.rotation,
                        _timelineOrigin == origin ? _appliedVerticalOffset : 0,
                        _timelineOrigin == origin ? _appliedYawOffset : 0);
                }
                _timelineAnchorSource = target.Source;
                _timelineOrigin = origin;
                _timelineAnchorValid = true;
            }
            Vector3 position;
            Quaternion rotation;
            ComposeTimelinePose(target.Position, target.Rotation, _timelineLocalPosition, _timelineLocalRotation,
                _verticalOffset, _yawOffset, out position, out rotation);
            origin.rotation = rotation;
            origin.position = position;
            _appliedVerticalOffset = _verticalOffset;
            _appliedYawOffset = _yawOffset;
            _initialAlignmentPending = false;
            _initialAlignmentPoseValid = false;
            _previousCameraPose = target;
            _baselineValid = true;
            _timelineStateKnown = _timelineWasPlaying = true;
            return true;
        }

        private void CaptureTimelineAnchor(Vector3 cameraPosition, Quaternion cameraRotation,
            Vector3 originPosition, Quaternion originRotation, float height, float yaw)
        {
            Quaternion inverse = Quaternion.Inverse(Quaternion.AngleAxis(yaw, Vector3.up) * cameraRotation);
            _timelineLocalPosition = inverse * (originPosition - cameraPosition - Vector3.up * height);
            _timelineLocalRotation = inverse * originRotation;
        }

        private static void ComposeTimelinePose(Vector3 cameraPosition, Quaternion cameraRotation, Vector3 anchorPosition,
            Quaternion anchorRotation, float height, float yaw, out Vector3 position, out Quaternion rotation)
        {
            Quaternion basis = Quaternion.AngleAxis(yaw, Vector3.up) * cameraRotation;
            rotation = basis * anchorRotation;
            position = cameraPosition + Vector3.up * height + basis * anchorPosition;
        }

        // Invoked by the opt-in in-game validator using the real Unity math runtime.
        internal static bool ValidateTimelinePoseMath()
        {
            Vector3 camera = new Vector3(3, 2, 5), anchor = new Vector3(0, -1.6f, 0);
            Vector3 position;
            Quaternion rotation;
            ComposeTimelinePose(camera, Quaternion.identity, anchor, Quaternion.identity, 0, 0, out position, out rotation);
            Vector3 baseline = position;
            // The neutral tracked head is exactly on the authored camera.
            if (Vector3.Distance(position + rotation * -anchor, camera) > 0.00001f) return false;
            for (int i = 0; i < 10000; i++)
                ComposeTimelinePose(camera, Quaternion.identity, anchor, Quaternion.identity, 0, 0, out position, out rotation);
            if (Vector3.Distance(position, baseline) > 0.00001f) return false;
            ComposeTimelinePose(camera, Quaternion.identity, anchor, Quaternion.identity, 1.25f, 90, out position, out rotation);
            if (Vector3.Distance(position + rotation * -anchor, camera + Vector3.up * 1.25f) > 0.00001f) return false;
            if (Vector3.Distance(rotation * Vector3.forward, Vector3.right) > 0.00001f) return false;
            // Physical head movement remains relative to the animated origin.
            Vector3 trackedMotion = new Vector3(0.2f, 0.1f, 0.3f);
            if (Vector3.Distance((position + rotation * (-anchor + trackedMotion)) - (position + rotation * -anchor), rotation * trackedMotion) > 0.00001f) return false;
            ComposeTimelinePose(camera, Quaternion.identity, anchor, Quaternion.identity, 0, 0, out position, out rotation);
            return Vector3.Distance(position, baseline) < 0.00001f && Quaternion.Angle(rotation, Quaternion.identity) < 0.001f;
        }
    }
}
#endif
