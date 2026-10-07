#if KKS
using System;
using System.Reflection;
using UnityEngine;

namespace KK_VR_CameraSync
{
    // KKS port of the wrist-menu contract. Rebuild from one camera-local
    // anchor each frame so user offsets and physical tracking never accumulate.
    internal sealed partial class CameraSyncDriver
    {
        internal static bool TimelineDisabled = true;
        private bool _timelineSuppressed, _externalOwner, _hasFovOverride, _fovOverrideEnabled;
        private float _fovOverride = 53.13f, _verticalOffset, _yawOffset;
        internal const int ExternalOwnerLeaseTicks = 45;
        private const int TimelinePoseMissLimit = 8;
        private const float TimelinePositionHoldMeters = 0.000001f;
        private const float TimelineRotationHoldDegrees = 0.001f;

        private bool _timelineAnchorValid;
        private Vector3 _timelineLocalPosition;
        private Quaternion _timelineLocalRotation;
        private CameraPoseSource _timelineAnchorSource;
        private Transform _timelineOrigin;
        private float _appliedVerticalOffset, _appliedYawOffset;
        private int _externalOwnerRenewals;
        private int _timelinePoseMisses;
        private bool _timelineHoldValid;
        private Vector3 _timelineHoldPosition;
        private Quaternion _timelineHoldRotation;

        internal void SetTimelineCameraSuppressed(bool value)
        {
            if (_timelineSuppressed == value) return;
            _timelineSuppressed = value;
            ResetBaseline();
        }
        internal void SetExternalVrCameraOwner(bool value)
        {
            if (value)
            {
                // Callers renew this every frame while MMDD owns the rig. A
                // caller that stops renewing cannot leave CameraSync yielded.
                _externalOwnerRenewals = 0;
                if (_externalOwner)
                    return;
                _externalOwner = true;
                ResetBaseline();
                return;
            }

            if (!_externalOwner)
                return;
            _externalOwner = false;
            _externalOwnerRenewals = 0;
            ResetBaseline();
        }

        private static Type _mmddBridgeType;
        private static PropertyInfo _mmddIsPlayingProp;
        private static PropertyInfo _mmddDirectOwnerProp;
        private static bool _mmddBridgeResolved;
        private static int _lastMmddResolveTick;

        private static bool IsMmdVrCameraActive()
        {
            int now = Environment.TickCount;
            if (!_mmddBridgeResolved && (now - _lastMmddResolveTick > 1000 || _lastMmddResolveTick == 0))
            {
                _lastMmddResolveTick = now;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = assembly.GetType("KKCharaStudioVR.VRMmddStateBridge", false);
                    if (t != null)
                    {
                        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
                        _mmddBridgeType = t;
                        _mmddIsPlayingProp = t.GetProperty("PlaybackIsPlaying", flags);
                        _mmddDirectOwnerProp = t.GetProperty("DirectVrCameraOwner", flags);
                        _mmddBridgeResolved = true;
                        break;
                    }
                }
            }

            if (_mmddBridgeType == null || _mmddIsPlayingProp == null)
                return false;

            try
            {
                bool isPlaying = (bool)_mmddIsPlayingProp.GetValue(null, null);
                if (!isPlaying) return false;
                bool directOwner = _mmddDirectOwnerProp != null && (bool)_mmddDirectOwnerProp.GetValue(null, null);
                return directOwner;
            }
            catch
            {
                return false;
            }
        }

        private bool ExternalOwnerHeld()
        {
            return (_externalOwner && _externalOwnerRenewals < ExternalOwnerLeaseTicks) || IsMmdVrCameraActive();
        }

        private bool ConsumeExternalOwnerLease()
        {
            if (!_externalOwner)
                return false;
            if (_externalOwnerRenewals >= ExternalOwnerLeaseTicks)
            {
                _externalOwner = false;
                _externalOwnerRenewals = 0;
                _timelineAnchorValid = false;
                _baselineValid = false;
                _timelineHoldValid = false;
                return false;
            }

            _externalOwnerRenewals++;
            return true;
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
            if (TimelineDisabled) return false;
            bool playing;
            Transform origin, head;
            return Plugin.Instance != null && Plugin.Instance.SyncEnabled.Value && !IsSuspended && !IsSceneLoading()
                && !_timelineSuppressed && !ExternalOwnerHeld() && !IsMmdVrCameraActive() && TryGetVrRig(out origin, out head)
                && TryGetTimelinePlaybackState(out playing) && playing;
        }

        private bool ApplyKksTimeline(Transform origin, Transform head, bool playing)
        {
            // MMDD owns the rig until it renews the lease. Yielding here is what
            // keeps the two writers from alternating the origin every frame.
            if (ConsumeExternalOwnerLease() || IsMmdVrCameraActive())
            {
                _timelineAnchorValid = _baselineValid = false;
                _timelineHoldValid = false;
                return true;
            }
            if (TimelineDisabled)
            {
                _timelineAnchorValid = _baselineValid = false;
                _timelineHoldValid = false;
                _timelinePoseMisses = 0;
                return false;
            }
            if (playing && _timelineSuppressed)
            {
                _timelineAnchorValid = _baselineValid = false;
                _timelineHoldValid = false;
                return true;
            }
            if (!playing)
            {
                if (_timelineAnchorValid) _baselineValid = false;
                _timelineAnchorValid = false;
                _timelineHoldValid = false;
                _timelinePoseMisses = 0;
                return false;
            }
            Plugin plugin = Plugin.Instance;
            CameraPose target;
            bool compensate = _hasFovOverride ? _fovOverrideEnabled : plugin.CompensateTimelineFov.Value;
            float fov = _hasFovOverride ? _fovOverride : plugin.TimelineReferenceFov.Value;
            if (!TryGetStudioCameraPose(compensate, fov, out target))
            {
                // One dropped sample used to invalidate the anchor. The next
                // frame then treated playback as a new start and snapped the
                // headset. Hold the last anchor through a short gap instead.
                if (_timelineAnchorValid && _timelinePoseMisses < TimelinePoseMissLimit)
                {
                    _timelinePoseMisses++;
                    return true;
                }
                _timelineAnchorValid = _baselineValid = false;
                _timelineHoldValid = false;
                return true;
            }
            _timelinePoseMisses = 0;
            CameraRotationMode mode = plugin.TimelineFullRotation.Value ? CameraRotationMode.Full : plugin.RotationMode.Value;
            target.Rotation = FilterRotation(target.Rotation, mode);
            if (!_timelineAnchorValid || _timelineAnchorSource != target.Source || _timelineOrigin != origin)
            {
                bool firstAcquire = !_timelineAnchorValid;
                if (firstAcquire && plugin.AlignOnTimelinePlay.Value)
                {
                    SnapHeadToTarget(origin, head, target.Position, target.Rotation, mode);
                    CaptureTimelineAnchor(target.Position, target.Rotation, origin.position, origin.rotation, 0, 0);
                }
                else
                {
                    // The current origin may already include wrist-menu offsets
                    // from the previous play session. Remove them when capturing
                    // the anchor so resume/source changes do not apply them twice.
                    // A source or rig change mid-playback rebases the same way.
                    // Snapping there made the view hitch whenever the camera
                    // object flickered.
                    CaptureTimelineAnchor(target.Position, target.Rotation, origin.position, origin.rotation,
                        _timelineOrigin == origin ? _appliedVerticalOffset : 0,
                        _timelineOrigin == origin ? _appliedYawOffset : 0);
                }
                _timelineAnchorSource = target.Source;
                _timelineOrigin = origin;
                _timelineAnchorValid = true;
                _timelineHoldValid = false;
            }
            Vector3 position;
            Quaternion rotation;
            ComposeTimelinePose(target.Position, target.Rotation, _timelineLocalPosition, _timelineLocalRotation,
                _verticalOffset, _yawOffset, out position, out rotation);
            // The composed pose is absolute. Rewriting an unchanged origin still
            // dirties the tracking rig and every child canvas for the whole frame.
            // The hold must stay under a fraction of a millimetre and a thousandth
            // of a degree. The previous 0.1 mm / 0.02° gate stepped slow pans and
            // FOV dolly motion at a fraction of the headset rate.
            bool unchanged = _timelineHoldValid
                && Vector3.Distance(_timelineHoldPosition, position) <= TimelinePositionHoldMeters
                && Quaternion.Angle(_timelineHoldRotation, rotation) <= TimelineRotationHoldDegrees
                && Vector3.Distance(origin.position, position) <= TimelinePositionHoldMeters
                && Quaternion.Angle(origin.rotation, rotation) <= TimelineRotationHoldDegrees;
            if (!unchanged)
            {
                origin.rotation = rotation;
                origin.position = position;
                _timelineHoldPosition = position;
                _timelineHoldRotation = rotation;
                _timelineHoldValid = true;
            }
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
