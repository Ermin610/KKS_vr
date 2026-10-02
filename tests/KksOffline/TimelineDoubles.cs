using UnityEngine;

namespace KK_VR_CameraSync
{
    internal enum CameraPoseSource { CameraData, ObjectCamera }
    internal enum CameraRotationMode { Full, YawOnly, None }
    internal struct CameraPose { public Vector3 Position; public Quaternion Rotation; public CameraPoseSource Source; }
    internal sealed class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    internal sealed class Plugin
    {
        public static Plugin Instance = new();
        public Setting<bool> SyncEnabled = new(true), CompensateTimelineFov = new(false), TimelineFullRotation = new(true), AlignOnTimelinePlay = new(false);
        public Setting<float> TimelineReferenceFov = new(53.13f);
        public Setting<CameraRotationMode> RotationMode = new(CameraRotationMode.Full);
    }
    internal sealed partial class CameraSyncDriver
    {
        // Replace only the surrounding game services, retaining the production
        // ApplyKksTimeline and public wrist-menu methods in this partial class.
        private bool _baselineValid, _initialAlignmentPending, _initialAlignmentPoseValid, _timelineStateKnown, _timelineWasPlaying;
        private CameraPose _previousCameraPose;
        public static bool Loading, RigAvailable = true;
        public bool Playing = true, PoseAvailable = true, IsSuspended;
        public CameraPose Target = new() { Rotation = Quaternion.identity };
        public int SnapCalls;
        public static Transform RigOrigin = new(), RigHead = new();
        public void ResetBaseline() { _baselineValid = _timelineAnchorValid = false; }
        public void ResumeAndReset() { IsSuspended = false; ResetBaseline(); }
        private static bool IsSceneLoading() => Loading;
        private static bool TryGetVrRig(out Transform origin, out Transform head) { origin = RigOrigin; head = RigHead; return RigAvailable && VRGIN.Core.VR.Active; }
        private bool TryGetTimelinePlaybackState(out bool playing) { playing = Playing; return true; }
        private bool TryGetStudioCameraPose(bool compensate, float fov, out CameraPose target) { target = Target; return PoseAvailable; }
        private static Quaternion FilterRotation(Quaternion q, CameraRotationMode mode) => mode == CameraRotationMode.None ? Quaternion.identity : q;
        private void SnapHeadToTarget(Transform origin, Transform head, Vector3 position, Quaternion rotation, CameraRotationMode mode)
        {
            // Fixed neutral head in this harness. Actual tracked-head alignment
            // belongs to the Unity integration test, not this simulation.
            SnapCalls++;
            origin.position = position;
            origin.rotation = rotation;
        }
        public bool Tick(Transform origin, bool playing = true) => ApplyKksTimeline(origin, RigHead, playing);
    }
}
