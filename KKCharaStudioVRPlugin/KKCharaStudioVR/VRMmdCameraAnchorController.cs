using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR;

/// <summary>
/// MMDD's legacy VR path recentres the current eye on every camera evaluation.
/// Capture one neutral room-scale offset at playback start and restore it after
/// MMDD has authored the frame, so leaning or stepping does not become a new
/// camera centre while Fixed FOV changes the composition distance.
/// Right-stick yaw, pitch, and height are reapplied from that authored pose
/// after the correction, so the nudge cannot accumulate into the MMD camera.
/// </summary>
[DefaultExecutionOrder(31900)]
internal sealed class VRMmdCameraAnchorController : MonoBehaviour
{
    private const float ReporterStaleSeconds = 1f;
    private const float MaxLocalOffset = 5f;
    private const float AppliedRotationMatchDegrees = 0.05f;
    private const float AppliedPositionMatchMeters = 0.004f;

    private static VRMmdCameraAnchorController _instance;

    private bool _anchorValid;
    private Vector3 _neutralHeadOriginLocal;
    private int _playbackGeneration = -1;
    private bool _preparedNeutralValid;
    private Vector3 _preparedNeutralHeadOriginLocal;
    private Vector3 _preparedOriginPosition;
    private Quaternion _preparedOriginRotation = Quaternion.identity;
    private bool _presentationYawApplied;
    private Vector3 _presentationYawBasePosition;
    private Quaternion _presentationYawBaseRotation = Quaternion.identity;
    private Vector3 _presentationYawAppliedPosition;
    private Quaternion _presentationYawAppliedRotation = Quaternion.identity;

    private void Awake()
    {
        _instance = this;
    }

    private void LateUpdate()
    {
        bool reporterFresh = VRMmddStateBridge.PlaybackReported
            && Time.realtimeSinceStartup - VRMmddStateBridge.PlaybackReportRealtime
                <= ReporterStaleSeconds;
        bool mmddOwnsVrCamera = reporterFresh
            && VRMmddStateBridge.PlaybackAvailable
            && VRMmddStateBridge.PlaybackIsPlaying
            && VRMmddStateBridge.DirectVrCameraOwner;

        // A followed Timeline camera is the single final writer when both
        // systems happen to play together.
        if (VRTimelineCameraFollowController.IsTimelineCameraWriterActive)
        {
            ClearAnchor();
            ClearPresentationYawTracking();
            return;
        }

        if (!mmddOwnsVrCamera)
        {
            ClearAnchor();
            // Without a camera motion the rig belongs to the player's own
            // locomotion. A trim left from a camera clip must not keep turning it.
            RestorePresentationYawIfStillApplied();
            return;
        }

        // Height and pitch move the eye. Put the rig back on the untrimmed pose
        // before the room-scale correction reads head.position as the authored
        // camera, then CorrectCurrentEvaluation reapplies the nudge.
        RestorePresentationYawIfStillApplied();
        CorrectCurrentEvaluation();
    }

    private void OnDisable()
    {
        ClearAnchor();
        RestorePresentationYawIfStillApplied();
    }

    private void OnDestroy()
    {
        ClearAnchor();
        RestorePresentationYawIfStillApplied();
        if (_instance == this)
            _instance = null;
    }

    /// <summary>
    /// Captures the tracked head offset before an operation that can synchronously
    /// evaluate MMDD's legacy VR camera. Capturing after mmdd.update(True) is too
    /// late: at that point MMDD has already made the current eye its new centre.
    /// </summary>
    internal static bool PrepareForSynchronousCameraEvaluation()
    {
        if (_instance == null
            || !VRMmddStateBridge.PlaybackReported
            || !VRMmddStateBridge.PlaybackAvailable
            || !VRMmddStateBridge.DirectVrCameraOwner
            || Time.realtimeSinceStartup - VRMmddStateBridge.PlaybackReportRealtime
                > ReporterStaleSeconds
            || VRTimelineCameraFollowController.IsTimelineCameraWriterActive)
        {
            return false;
        }

        return _instance.CapturePreparedNeutral();
    }

    /// <summary>
    /// Package loading can create the first direct VR camera, so there may be no
    /// prior ownership report to gate the pre-evaluation snapshot. The caller
    /// must refresh ownership after execution before applying the correction.
    /// </summary>
    internal static bool PrepareForPotentialDirectCameraEvaluation()
    {
        if (_instance == null
            || VRTimelineCameraFollowController.IsTimelineCameraWriterActive)
        {
            return false;
        }

        return _instance.CapturePreparedNeutral();
    }

    /// <summary>
    /// Fixed-FOV edits while MMDD is paused synchronously evaluate one camera
    /// frame. Correct that one legacy recenter immediately; LateUpdate will not
    /// run the continuous path because playback is already reported as paused.
    /// </summary>
    internal static void CorrectAfterSynchronousCameraEvaluation()
    {
        if (_instance == null
            || VRTimelineCameraFollowController.IsTimelineCameraWriterActive)
        {
            CancelPreparedSynchronousCameraEvaluation();
            return;
        }

        if (!VRMmddStateBridge.DirectVrCameraOwner)
        {
            CancelPreparedSynchronousCameraEvaluation();
            return;
        }

        _instance.CorrectCurrentEvaluation();
    }

    internal static void CancelPreparedSynchronousCameraEvaluation()
    {
        if (_instance != null)
            _instance.RestoreAndClearPreparedOrigin();
    }

    internal static void ResetForSceneTransition()
    {
        VRMmdCameraAnchorController[] controllers =
            Object.FindObjectsOfType<VRMmdCameraAnchorController>();
        foreach (VRMmdCameraAnchorController controller in controllers)
        {
            controller.ClearAnchor();
            controller.ClearPresentationYawTracking();
        }
    }

    internal static void RefreshPresentationYawNow()
    {
        if (_instance == null)
            return;
        if (VRMmdPlaybackController.HasActiveCameraMotion)
            _instance.ApplyPresentationYawToCurrentRig();
        else
            _instance.RestorePresentationYawIfStillApplied();
    }

    internal static void ReleasePresentationYaw()
    {
        if (_instance != null)
            _instance.RestorePresentationYawIfStillApplied();
    }

    private void ClearAnchor()
    {
        _anchorValid = false;
        _neutralHeadOriginLocal = Vector3.zero;
        _playbackGeneration = -1;
        _preparedNeutralValid = false;
        _preparedNeutralHeadOriginLocal = Vector3.zero;
        _preparedOriginPosition = Vector3.zero;
        _preparedOriginRotation = Quaternion.identity;
    }

    private bool CapturePreparedNeutral()
    {
        Transform origin;
        Transform head;
        if (!TryGetVrRig(out origin, out head))
        {
            _preparedNeutralValid = false;
            return false;
        }

        Vector3 local = origin.InverseTransformPoint(head.position);
        if (!IsFinite(local) || local.sqrMagnitude > MaxLocalOffset * MaxLocalOffset)
        {
            _preparedNeutralValid = false;
            return false;
        }

        _preparedNeutralHeadOriginLocal = local;
        _preparedOriginPosition = origin.position;
        _preparedOriginRotation = origin.rotation;
        _preparedNeutralValid = true;
        return true;
    }

    private void RestoreAndClearPreparedOrigin()
    {
        if (!_preparedNeutralValid)
            return;

        Transform origin;
        Transform head;
        if (TryGetVrRig(out origin, out head)
            && IsFinite(_preparedOriginPosition)
            && IsFinite(_preparedOriginRotation))
        {
            origin.rotation = _preparedOriginRotation;
            origin.position = _preparedOriginPosition;
        }

        _preparedNeutralValid = false;
        _preparedNeutralHeadOriginLocal = Vector3.zero;
        _preparedOriginPosition = Vector3.zero;
        _preparedOriginRotation = Quaternion.identity;
    }

    private void CorrectCurrentEvaluation()
    {
        Transform origin;
        Transform head;
        if (!TryGetVrRig(out origin, out head))
        {
            ClearAnchor();
            return;
        }

        int generation = VRMmddStateBridge.PlaybackGeneration;
        if (_preparedNeutralValid)
        {
            _neutralHeadOriginLocal = _preparedNeutralHeadOriginLocal;
            _playbackGeneration = generation;
            _anchorValid = true;
            _preparedNeutralValid = false;
            _preparedOriginPosition = Vector3.zero;
            _preparedOriginRotation = Quaternion.identity;
        }
        else if (!_anchorValid || _playbackGeneration != generation)
        {
            Vector3 local = origin.InverseTransformPoint(head.position);
            if (!IsFinite(local) || local.sqrMagnitude > MaxLocalOffset * MaxLocalOffset)
            {
                ClearAnchor();
                return;
            }

            _neutralHeadOriginLocal = local;
            _playbackGeneration = generation;
            _anchorValid = true;
            VRLog.Info("MMDD VR camera captured a neutral head-position anchor.");
        }

        // After MMDD's legacy correction, head.position is the authored camera
        // position. Rebuild the origin using the captured neutral offset rather
        // than the current tracking offset, preserving subsequent physical motion.
        Vector3 authoredCameraPosition = head.position;
        Vector3 neutralWorldOffset = origin.TransformVector(_neutralHeadOriginLocal);
        Vector3 nextOriginPosition = authoredCameraPosition - neutralWorldOffset;
        if (IsFinite(nextOriginPosition))
        {
            if ((origin.position - nextOriginPosition).sqrMagnitude > 1e-10f)
                origin.position = nextOriginPosition;
            if (VRMmdPlaybackController.IsPresentationCameraControlActive)
                ApplyPresentationYaw(origin, head);
            else
                RestorePresentationYawIfStillApplied();
        }
        else
            ClearAnchor();
    }

    private void ApplyPresentationYawToCurrentRig()
    {
        Transform origin;
        Transform head;
        if (!TryGetVrRig(out origin, out head))
            return;
        ApplyPresentationYaw(origin, head);
    }

    private void ApplyPresentationYaw(Transform origin, Transform head)
    {
        if (!IsFinite(origin.position) || !IsFinite(origin.rotation) || head == null)
        {
            ClearPresentationYawTracking();
            return;
        }

        bool poseMatches = PresentationPoseMatches(
            origin.position,
            origin.rotation,
            _presentationYawAppliedPosition,
            _presentationYawAppliedRotation);
        Vector3 basePosition = poseMatches ? _presentationYawBasePosition : origin.position;
        Quaternion baseRotation = poseMatches ? _presentationYawBaseRotation : origin.rotation;
        if (!IsFinite(basePosition) || !IsFinite(baseRotation))
        {
            ClearPresentationYawTracking();
            return;
        }

        float yaw = VRMmdPlaybackController.PresentationYawOffset;
        float pitch = VRMmdPlaybackController.PresentationPitchOffset;
        float height = VRMmdPlaybackController.PresentationHeightOffset;
        float distance = VRMmdPlaybackController.PresentationDistanceOffset;
        bool anyTrim = Mathf.Abs(yaw) >= 0.001f
            || Mathf.Abs(pitch) >= 0.001f
            || Mathf.Abs(height) >= 0.0001f
            || Mathf.Abs(distance) >= 0.0001f;
        if (!anyTrim)
        {
            if (poseMatches)
            {
                origin.rotation = baseRotation;
                origin.position = basePosition;
            }
            ClearPresentationYawTracking();
            return;
        }

        // Rebuild from the authored rig every frame. Yaw and pitch orbit the
        // eye so the MMD camera centre stays put; height is the only shift.
        origin.rotation = baseRotation;
        origin.position = basePosition;
        Vector3 headPosition = head.position;
        if (!IsFinite(headPosition))
        {
            ClearPresentationYawTracking();
            return;
        }

        if (Mathf.Abs(yaw) >= 0.001f)
            origin.RotateAround(headPosition, Vector3.up, yaw);
        if (Mathf.Abs(pitch) >= 0.001f)
        {
            Vector3 pitchAxis = head.right;
            if (pitchAxis.sqrMagnitude > 0.0001f)
                origin.RotateAround(head.position, pitchAxis, pitch);
        }
        if (Mathf.Abs(height) >= 0.0001f)
            origin.position += Vector3.up * height;
        if (Mathf.Abs(distance) >= 0.0001f)
        {
            Vector3 forward = head.forward;
            if (forward.sqrMagnitude > 0.0001f)
                origin.position += forward.normalized * distance;
        }

        if (!IsFinite(origin.position) || !IsFinite(origin.rotation))
        {
            origin.rotation = baseRotation;
            origin.position = basePosition;
            ClearPresentationYawTracking();
            return;
        }

        _presentationYawBasePosition = basePosition;
        _presentationYawBaseRotation = baseRotation;
        _presentationYawAppliedPosition = origin.position;
        _presentationYawAppliedRotation = origin.rotation;
        _presentationYawApplied = true;
    }

    private void ClearPresentationYawTracking()
    {
        _presentationYawApplied = false;
        _presentationYawBasePosition = Vector3.zero;
        _presentationYawBaseRotation = Quaternion.identity;
        _presentationYawAppliedPosition = Vector3.zero;
        _presentationYawAppliedRotation = Quaternion.identity;
    }

    private void RestorePresentationYawIfStillApplied()
    {
        if (!_presentationYawApplied)
            return;

        Transform origin;
        Transform head;
        if (TryGetVrRig(out origin, out head)
            && IsFinite(origin.position)
            && IsFinite(origin.rotation)
            && IsFinite(_presentationYawBasePosition)
            && IsFinite(_presentationYawBaseRotation)
            && PresentationPoseMatches(
                origin.position,
                origin.rotation,
                _presentationYawAppliedPosition,
                _presentationYawAppliedRotation))
        {
            origin.rotation = _presentationYawBaseRotation;
            origin.position = _presentationYawBasePosition;
        }

        ClearPresentationYawTracking();
    }

    private static bool PresentationPoseMatches(
        Vector3 position,
        Quaternion rotation,
        Vector3 appliedPosition,
        Quaternion appliedRotation)
    {
        return Vector3.Distance(position, appliedPosition) <= AppliedPositionMatchMeters
            && Quaternion.Angle(rotation, appliedRotation) <= AppliedRotationMatchDegrees;
    }

    private static bool TryGetVrRig(out Transform origin, out Transform head)
    {
        origin = null;
        head = null;
        if (!VR.Active || VR.Camera == null)
            return false;

        origin = VR.Camera.Origin;
        head = VR.Camera.Head;
        return origin != null && head != null;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z)
            && !float.IsNaN(value.w) && !float.IsInfinity(value.w);
    }
}
