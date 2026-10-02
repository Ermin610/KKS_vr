using System;
using System.Reflection;
using Manager;
using Studio;
using UnityEngine;
using VRGIN.Core;

namespace KK_VR_CameraSync
{
    public enum CameraRotationMode
    {
        Full,
        YawOnly,
        None
    }

    public enum PositionFollowMode
    {
        AllMotion,
        CutsOnly,
        Off
    }

    internal enum CameraPoseSource
    {
        CameraData,
        ObjectCamera
    }

    internal struct CameraPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public CameraPoseSource Source;
        public float SourceFieldOfView;
        public float EffectiveDistanceZ;
        public bool FovCompensated;
    }

    [DefaultExecutionOrder(32000)]
    internal sealed partial class CameraSyncDriver : MonoBehaviour
    {
        private const float PositionChangeThreshold = 0.0001f;
        private const float RotationChangeThreshold = 0.02f;

        private bool _baselineValid;
        private CameraPose _previousCameraPose;
        private bool _initialAlignmentPending;
        private bool _initialAlignmentPoseValid;
        private CameraPose _initialAlignmentPose;
        private int _initialAlignmentReadyFrame = -1;
        private string _initialAlignmentReason = "Studio scene loaded";
        private int _suspendDepth;
        private bool _nativeSceneLoadPending;
        private int _nativeSceneLoadDepth;
        private int _nativeSceneLoadStartFrame = -1;
        private bool _sceneInfoObserved;
        private object _lastSceneInfo;
        private int _resumeFrame = -1;
        private float _nextErrorLogTime;
        private bool _timelineStateKnown;
        private bool _timelineWasPlaying;
        private PropertyInfo _timelineIsPlayingProperty;
        private float _nextTimelineResolveTime;

        internal bool IsSuspended
        {
            get
            {
                return _suspendDepth > 0 ||
                       _nativeSceneLoadPending ||
                       Time.frameCount < _resumeFrame;
            }
        }

        internal void Suspend()
        {
            _suspendDepth++;
            _baselineValid = false;
            ResetTimelineObservation();
        }

        internal void ResumeAndReset()
        {
            if (_suspendDepth > 0)
                _suspendDepth--;

            _baselineValid = false;
            ResetTimelineObservation();
            // Let CameraControl, scene hooks, and SteamVR settle before accepting
            // the next camera pose as the new baseline.
            _resumeFrame = Math.Max(_resumeFrame, Time.frameCount + 2);
        }

        internal void ResetBaseline()
        {
            _baselineValid = false;
            ResetTimelineObservation();
            _resumeFrame = Math.Max(_resumeFrame, Time.frameCount + 1);
        }

        internal void BeginNativeSceneLoad()
        {
            _nativeSceneLoadDepth++;
            if (_nativeSceneLoadDepth > 1)
                return;

            _nativeSceneLoadPending = true;
            _nativeSceneLoadStartFrame = Time.frameCount;
            _baselineValid = false;
            ResetTimelineObservation();
            _initialAlignmentPending = false;
            _initialAlignmentPoseValid = false;
            _initialAlignmentReadyFrame = -1;
        }

        internal void CompleteNativeSceneLoad(bool succeeded)
        {
            if (_nativeSceneLoadDepth > 0)
                _nativeSceneLoadDepth--;

            if (_nativeSceneLoadDepth > 0)
                return;

            _nativeSceneLoadPending = false;
            _nativeSceneLoadStartFrame = -1;
            _baselineValid = false;
            ResetTimelineObservation();
            _resumeFrame = Math.Max(_resumeFrame, Time.frameCount + 2);

            if (!succeeded)
                return;

            CameraPose capturedPose;
            if (TryGetSceneInitialCameraPose(out capturedPose))
            {
                _initialAlignmentPose = capturedPose;
                _initialAlignmentPoseValid = true;
            }

            RequestInitialAlignment("Studio scene loaded");
        }

        internal void CompleteNativeCameraReset()
        {
            _baselineValid = false;
            _resumeFrame = Math.Max(_resumeFrame, Time.frameCount + 2);
        }

        private void RequestInitialAlignment(string reason)
        {
            _initialAlignmentPending = true;
            _initialAlignmentReadyFrame =
                Math.Max(_initialAlignmentReadyFrame, Time.frameCount + 2);
            _initialAlignmentReason = reason;
            _baselineValid = false;
        }

        private void LateUpdate()
        {
            if (ObserveStudioSceneInfoChange())
            {
                HandleObservedStudioSceneChange();
                return;
            }

            RecoverStaleSceneLoadSuspension();

            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.SyncEnabled.Value)
            {
                _baselineValid = false;
                ResetTimelineObservation();
                return;
            }

            if (IsSuspended || IsSceneLoading())
            {
                _baselineValid = false;
                ResetTimelineObservation();
                return;
            }

            try
            {
                Transform origin;
                Transform head;
                if (!TryGetVrRig(out origin, out head))
                {
                    _baselineValid = false;
                    ResetTimelineObservation();
                    return;
                }

                bool timelinePlaying;
                bool timelineStateAvailable =
                    TryGetTimelinePlaybackState(out timelinePlaying);
#if KKS
                if (ApplyKksTimeline(origin, head, timelinePlaying)) return;
#endif
                bool timelineStarted = false;
                bool timelineStopped = false;
                if (timelineStateAvailable)
                {
                    timelineStarted = timelinePlaying &&
                                      (!_timelineStateKnown || !_timelineWasPlaying);
                    timelineStopped = !timelinePlaying &&
                                      _timelineStateKnown && _timelineWasPlaying;
                    _timelineStateKnown = true;
                    _timelineWasPlaying = timelinePlaying;
                }
                else
                {
                    timelinePlaying = false;
                    ResetTimelineObservation();
                }

                CameraRotationMode followRotationMode =
                    timelinePlaying && plugin.TimelineFullRotation.Value
                        ? CameraRotationMode.Full
                        : plugin.RotationMode.Value;
                bool compensateTimelineFov =
                    timelinePlaying && plugin.CompensateTimelineFov.Value;

                CameraPose currentCameraPose;
                if (!TryGetStudioCameraPose(
                        compensateTimelineFov,
                        plugin.TimelineReferenceFov.Value,
                        out currentCameraPose))
                {
                    _baselineValid = false;
                    return;
                }

                CameraPose fullCurrentCameraPose = currentCameraPose;
                currentCameraPose.Rotation =
                    FilterRotation(
                        currentCameraPose.Rotation,
                        followRotationMode);

                if (timelineStarted && plugin.AlignOnTimelinePlay.Value)
                {
                    CameraRotationMode alignmentMode =
                        plugin.TimelineFullRotation.Value
                            ? CameraRotationMode.Full
                            : plugin.RotationMode.Value;
                    Quaternion alignmentRotation = FilterRotation(
                        fullCurrentCameraPose.Rotation,
                        alignmentMode);
                    SnapHeadToTarget(
                        origin,
                        head,
                        fullCurrentCameraPose.Position,
                        alignmentRotation,
                        alignmentMode);

                    _initialAlignmentPending = false;
                    _initialAlignmentPoseValid = false;
                    _initialAlignmentReadyFrame = -1;
                    _previousCameraPose = currentCameraPose;
                    _baselineValid = true;

                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogInfo(
                            "Timeline playback aligned the neutral VR view to the desktop camera. " +
                            "Source=" + currentCameraPose.Source +
                            ", position=" + FormatVector(currentCameraPose.Position) +
                            ", rotationMode=" + alignmentMode +
                            ", desktopFov=" + currentCameraPose.SourceFieldOfView.ToString("F2") +
                            ", effectiveDistanceZ=" + currentCameraPose.EffectiveDistanceZ.ToString("F3") +
                            ", fovCompensated=" + currentCameraPose.FovCompensated +
                            ".");
                    }

                    return;
                }

                if (timelineStopped)
                {
                    // The Timeline pose used full rotation and possibly a
                    // compensated distance. Start generic observation from a
                    // fresh baseline without applying a synthetic stop delta.
                    _baselineValid = false;
                }

                if (_initialAlignmentPending)
                {
                    if (!plugin.AlignInitialStudioCamera.Value)
                    {
                        _initialAlignmentPending = false;
                        _initialAlignmentPoseValid = false;
                        _initialAlignmentReadyFrame = -1;
                    }
                    else
                    {
                        if (Time.frameCount < _initialAlignmentReadyFrame)
                        {
                            _baselineValid = false;
                            return;
                        }

                        CameraPose targetPose =
                            _initialAlignmentPoseValid
                                ? _initialAlignmentPose
                                : fullCurrentCameraPose;
                        Quaternion initialRotation = FilterRotation(
                            targetPose.Rotation,
                            plugin.InitialAlignmentRotationMode.Value);
                        SnapHeadToTarget(
                            origin,
                            head,
                            targetPose.Position,
                            initialRotation,
                            plugin.InitialAlignmentRotationMode.Value);

                        _initialAlignmentPending = false;
                        _initialAlignmentPoseValid = false;
                        _initialAlignmentReadyFrame = -1;

                        CameraPose followBaseline = targetPose;
                        followBaseline.Rotation = FilterRotation(
                            followBaseline.Rotation,
                            followRotationMode);
                        _previousCameraPose = followBaseline;
                        _baselineValid = true;

                        // If an auto-playing Timeline advanced after the scene
                        // was loaded, apply that motion from the captured initial
                        // pose after the one-time alignment.
                        if (currentCameraPose.Source == followBaseline.Source)
                        {
                            float initialPositionDelta = Vector3.Distance(
                                currentCameraPose.Position,
                                followBaseline.Position);
                            float initialRotationDelta = Quaternion.Angle(
                                currentCameraPose.Rotation,
                                followBaseline.Rotation);
                            if (initialPositionDelta > PositionChangeThreshold ||
                                initialRotationDelta > RotationChangeThreshold)
                            {
                                ApplyCameraMotion(
                                    origin,
                                    head,
                                    currentCameraPose,
                                    initialPositionDelta,
                                    followRotationMode);
                            }
                        }

                        _previousCameraPose = currentCameraPose;

                        if (Plugin.Log != null)
                        {
                            Plugin.Log.LogInfo(
                                "Initial VR view aligned to Studio camera. Source=" +
                                currentCameraPose.Source +
                                ", reason=" +
                                _initialAlignmentReason +
                                ", position=" +
                                FormatVector(targetPose.Position) +
                                ", rotationMode=" +
                                plugin.InitialAlignmentRotationMode.Value +
                                ".");
                        }

                        return;
                    }
                }

                if (!_baselineValid)
                {
                    _previousCameraPose = currentCameraPose;
                    _baselineValid = true;
                    return;
                }

                if (currentCameraPose.Source !=
                    _previousCameraPose.Source)
                {
                    // CameraData and OCICamera are separate coordinate sources.
                    // Switching between them is not itself camera animation.
                    _previousCameraPose = currentCameraPose;
                    return;
                }

                float positionDelta = Vector3.Distance(
                    currentCameraPose.Position,
                    _previousCameraPose.Position);
                float rotationDelta = Quaternion.Angle(
                    currentCameraPose.Rotation,
                    _previousCameraPose.Rotation);
                bool cameraMoved =
                    positionDelta > PositionChangeThreshold ||
                    rotationDelta > RotationChangeThreshold;

                if (cameraMoved)
                {
                    ApplyCameraMotion(
                        origin,
                        head,
                        currentCameraPose,
                        positionDelta,
                        followRotationMode);
                }

                _previousCameraPose = currentCameraPose;
            }
            catch (Exception exception)
            {
                _baselineValid = false;
                LogExceptionThrottled(exception);
            }
        }

        private bool ObserveStudioSceneInfoChange()
        {
            Studio.Studio studio =
                Singleton<Studio.Studio>.Instance;
            object currentSceneInfo =
                studio == null
                    ? null
                    : (object)studio.sceneInfo;
            if (currentSceneInfo == null)
                return false;

            if (!_sceneInfoObserved)
            {
                // The first SceneInfo belongs to the empty Studio startup.
                // Remember it without moving the headset.
                _sceneInfoObserved = true;
                _lastSceneInfo = currentSceneInfo;
                return false;
            }

            if (ReferenceEquals(
                currentSceneInfo,
                _lastSceneInfo))
            {
                return false;
            }

            _lastSceneInfo = currentSceneInfo;
            return true;
        }

        private void HandleObservedStudioSceneChange()
        {
            _nativeSceneLoadPending = false;
            _nativeSceneLoadDepth = 0;
            _nativeSceneLoadStartFrame = -1;
            _baselineValid = false;
            ResetTimelineObservation();
            _initialAlignmentPending = false;
            _initialAlignmentPoseValid = false;
            _initialAlignmentReadyFrame = -1;

            CameraPose capturedPose;
            if (TryGetSceneInitialCameraPose(out capturedPose))
            {
                _initialAlignmentPose = capturedPose;
                _initialAlignmentPoseValid = true;
            }

            RequestInitialAlignment(
                "Studio sceneInfo changed");

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "Studio scene card change detected; " +
                    "initial VR alignment was scheduled.");
            }
        }

        private void RecoverStaleSceneLoadSuspension()
        {
            if (!_nativeSceneLoadPending ||
                _nativeSceneLoadStartFrame < 0 ||
                Time.frameCount - _nativeSceneLoadStartFrame <= 600 ||
                IsSceneLoading())
                return;

            _nativeSceneLoadPending = false;
            _nativeSceneLoadDepth = 0;
            _nativeSceneLoadStartFrame = -1;
            _baselineValid = false;
            ResetTimelineObservation();
            _resumeFrame = Time.frameCount + 2;
            RequestInitialAlignment("recovered Studio scene load");

            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning(
                    "A Studio scene load did not reach its postfix; " +
                    "camera synchronization recovered with a fresh alignment.");
            }
        }

        private void ApplyCameraMotion(
            Transform origin,
            Transform head,
            CameraPose currentCameraPose,
            float positionDelta,
            CameraRotationMode rotationMode)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            bool positionAuthoritative;
            switch (plugin.PositionMode.Value)
            {
                case PositionFollowMode.AllMotion:
                    positionAuthoritative = true;
                    break;
                case PositionFollowMode.CutsOnly:
                    positionAuthoritative =
                        positionDelta > plugin.CutPositionThreshold.Value;
                    break;
                default:
                    positionAuthoritative = false;
                    break;
            }

            if (plugin.PreserveHeadTracking.Value)
            {
                if (positionAuthoritative)
                {
                    ApplyCameraPoseDelta(origin, currentCameraPose);
                }
                else
                {
                    ApplyRotationDeltaKeepingHeadPosition(
                        origin,
                        head,
                        currentCameraPose.Rotation);
                }
            }
            else
            {
                if (positionAuthoritative)
                {
                    SnapHeadToTarget(
                        origin,
                        head,
                        currentCameraPose.Position,
                        currentCameraPose.Rotation,
                        rotationMode);
                }
                else
                {
                    RotateHeadToTargetKeepingPosition(
                        origin,
                        head,
                        currentCameraPose.Rotation,
                        rotationMode);
                }
            }

            if (plugin.PositionMode.Value == PositionFollowMode.CutsOnly &&
                positionAuthoritative &&
                Plugin.Log != null)
            {
                Plugin.Log.LogDebug(
                    "Camera cut accepted. Source=" +
                    currentCameraPose.Source +
                    ", distance=" +
                    positionDelta.ToString("F4") +
                    ".");
            }
        }

        /// <summary>
        /// Applies the Studio camera's frame-to-frame world-space delta to the
        /// VR origin. The current origin offset is retained, so physical head
        /// motion and user locomotion remain relative to the animated camera.
        /// </summary>
        private void ApplyCameraPoseDelta(
            Transform origin,
            CameraPose currentCameraPose)
        {
            Quaternion rotationDelta =
                currentCameraPose.Rotation *
                Quaternion.Inverse(_previousCameraPose.Rotation);

            Vector3 nextOriginPosition =
                currentCameraPose.Position +
                rotationDelta *
                (origin.position - _previousCameraPose.Position);
            Quaternion nextOriginRotation =
                rotationDelta * origin.rotation;

            origin.rotation = nextOriginRotation;
            origin.position = nextOriginPosition;
        }

        private void ApplyRotationDeltaKeepingHeadPosition(
            Transform origin,
            Transform head,
            Quaternion currentCameraRotation)
        {
            Vector3 headPosition = head.position;
            Quaternion rotationDelta =
                currentCameraRotation *
                Quaternion.Inverse(_previousCameraPose.Rotation);

            origin.rotation = rotationDelta * origin.rotation;
            origin.position += headPosition - head.position;
        }

        private static void SnapHeadToTarget(
            Transform origin,
            Transform head,
            Vector3 targetPosition,
            Quaternion targetRotation,
            CameraRotationMode rotationMode)
        {
            if (rotationMode != CameraRotationMode.None)
            {
                Quaternion currentHeadRotation =
                    rotationMode == CameraRotationMode.YawOnly
                        ? Quaternion.Euler(0f, head.rotation.eulerAngles.y, 0f)
                        : head.rotation;
                Quaternion rotationDelta =
                    targetRotation * Quaternion.Inverse(currentHeadRotation);
                origin.rotation = rotationDelta * origin.rotation;
            }

            origin.position += targetPosition - head.position;
        }

        private static void RotateHeadToTargetKeepingPosition(
            Transform origin,
            Transform head,
            Quaternion targetRotation,
            CameraRotationMode rotationMode)
        {
            if (rotationMode == CameraRotationMode.None)
                return;

            Vector3 headPosition = head.position;
            Quaternion currentHeadRotation =
                rotationMode == CameraRotationMode.YawOnly
                    ? Quaternion.Euler(0f, head.rotation.eulerAngles.y, 0f)
                    : head.rotation;
            Quaternion rotationDelta =
                targetRotation * Quaternion.Inverse(currentHeadRotation);

            origin.rotation = rotationDelta * origin.rotation;
            origin.position += headPosition - head.position;
        }

        private static Quaternion FilterRotation(
            Quaternion rotation,
            CameraRotationMode rotationMode)
        {
            switch (rotationMode)
            {
                case CameraRotationMode.Full:
                    return rotation;
                case CameraRotationMode.YawOnly:
                    return Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
                default:
                    return Quaternion.identity;
            }
        }

        private static bool TryGetVrRig(
            out Transform origin,
            out Transform head)
        {
            origin = null;
            head = null;

            if (!VR.Active || VR.Camera == null)
                return false;

            origin = VR.Camera.Origin;
            head = VR.Camera.Head;
            return origin != null && head != null;
        }

        private static bool IsSceneLoading()
        {
#if KKS
            return Scene.IsNowLoading || Scene.IsNowLoadingFade;
#else
            Scene scene = Singleton<Scene>.Instance;
            return scene != null &&
                   (scene.IsNowLoading || scene.IsNowLoadingFade);
#endif
        }

        private bool TryGetTimelinePlaybackState(out bool isPlaying)
        {
            isPlaying = false;

            if (_timelineIsPlayingProperty == null &&
                Time.unscaledTime >= _nextTimelineResolveTime)
            {
                _nextTimelineResolveTime = Time.unscaledTime + 1f;
                const BindingFlags flags =
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic;

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(
                            assembly.GetName().Name,
                            "Timeline",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    Type timelineType = assembly.GetType("Timeline.Timeline", false);
                    _timelineIsPlayingProperty =
                        timelineType == null
                            ? null
                            : timelineType.GetProperty("isPlaying", flags);
                    break;
                }
            }

            if (_timelineIsPlayingProperty == null)
                return false;

            try
            {
                object value = _timelineIsPlayingProperty.GetValue(null, null);
                if (!(value is bool))
                    return false;

                isPlaying = (bool)value;
                return true;
            }
            catch
            {
                _timelineIsPlayingProperty = null;
                _nextTimelineResolveTime = Time.unscaledTime + 1f;
                return false;
            }
        }

        private void ResetTimelineObservation()
        {
#if KKS
            _timelineAnchorValid = false;
#endif
            _timelineStateKnown = false;
            _timelineWasPlaying = false;
        }

        private bool TryGetStudioCameraPose(
            bool compensateFov,
            float referenceFov,
            out CameraPose pose)
        {
            pose = new CameraPose();

            Studio.Studio studio = Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.cameraCtrl == null)
                return false;

            Plugin plugin = Plugin.Instance;
            if (plugin != null &&
                plugin.ReadObjectCamera.Value &&
                TryGetActiveObjectCameraPose(studio, out pose))
            {
                return true;
            }

            Studio.CameraControl cameraControl = studio.cameraCtrl;
            Studio.CameraControl.CameraData cameraData = cameraControl.Export();
            return TryConvertCameraData(
                cameraControl,
                cameraData,
                compensateFov,
                referenceFov,
                out pose);
        }

        private bool TryGetSceneInitialCameraPose(
            out CameraPose pose)
        {
            pose = new CameraPose();

            Studio.Studio studio =
                Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.cameraCtrl == null)
                return false;

            Plugin plugin = Plugin.Instance;
            if (plugin != null &&
                plugin.ReadObjectCamera.Value &&
                TryGetActiveObjectCameraPose(studio, out pose))
            {
                return true;
            }

            Studio.CameraControl.CameraData savedCameraData =
                ReadMember(
                    studio.sceneInfo,
                    "cameraSaveData")
                as Studio.CameraControl.CameraData;
            if (savedCameraData != null)
            {
                return TryConvertCameraData(
                    studio.cameraCtrl,
                    savedCameraData,
                    false,
                    53.13f,
                    out pose);
            }

            return TryGetStudioCameraPose(false, 53.13f, out pose);
        }

        private static bool TryConvertCameraData(
            Studio.CameraControl cameraControl,
            Studio.CameraControl.CameraData cameraData,
            bool compensateFov,
            float referenceFov,
            out CameraPose pose)
        {
            pose = new CameraPose();
            if (cameraControl == null || cameraData == null)
                return false;

            Quaternion localRotation = Quaternion.Euler(cameraData.rotate);
            Vector3 effectiveDistance = cameraData.distance;
            float sourceFov = Mathf.Clamp(cameraControl.fieldOfView, 1f, 179f);
            float safeReferenceFov = Mathf.Clamp(referenceFov, 1f, 179f);
            if (compensateFov)
            {
                float sourceRate = Mathf.Tan(sourceFov * 0.5f * Mathf.Deg2Rad);
                float referenceRate = Mathf.Tan(safeReferenceFov * 0.5f * Mathf.Deg2Rad);
                if (Mathf.Abs(referenceRate) > 0.0001f)
#if KKS
                {
                    float distance = effectiveDistance.z * Mathf.Clamp(sourceRate / referenceRate, 0.1f, 10f);
                    effectiveDistance.z += Mathf.Clamp(distance - effectiveDistance.z, -10f, 10f);
                }
#else
                    effectiveDistance.z *= sourceRate / referenceRate;
#endif
            }
            Transform transformBase =
                ReadMember(cameraControl, "transBase") as Transform;

            if (transformBase != null)
            {
                pose.Rotation = transformBase.rotation * localRotation;
                pose.Position =
                    transformBase.TransformPoint(cameraData.pos) +
                    pose.Rotation * effectiveDistance;
            }
            else
            {
                pose.Rotation = localRotation;
                pose.Position =
                    cameraData.pos +
                    pose.Rotation * effectiveDistance;
            }

            pose.Source = CameraPoseSource.CameraData;
            pose.SourceFieldOfView = sourceFov;
            pose.EffectiveDistanceZ = effectiveDistance.z;
            pose.FovCompensated = compensateFov &&
                                  Mathf.Abs(effectiveDistance.z - cameraData.distance.z) > 0.0001f;
            return true;
        }

        private static bool TryGetActiveObjectCameraPose(
            Studio.Studio studio,
            out CameraPose pose)
        {
            pose = new CameraPose();

            object objectCamera = ReadMember(studio, "ociCamera");
            if (objectCamera == null)
                return false;

            object objectItem = ReadMember(objectCamera, "objectItem");
            Transform cameraTransform = objectItem as Transform;

            if (cameraTransform == null)
            {
                Component component = objectItem as Component;
                if (component != null)
                    cameraTransform = component.transform;
            }

            if (cameraTransform == null)
            {
                GameObject gameObject = objectItem as GameObject;
                if (gameObject != null)
                    cameraTransform = gameObject.transform;
            }

            if (cameraTransform == null)
                return false;

            pose.Position = cameraTransform.position;
            pose.Rotation = cameraTransform.rotation;
            pose.Source = CameraPoseSource.ObjectCamera;
            Camera camera = cameraTransform.GetComponentInChildren<Camera>();
            pose.SourceFieldOfView = camera == null ? 0f : camera.fieldOfView;
            pose.EffectiveDistanceZ = 0f;
            pose.FovCompensated = false;
            return true;
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic;

            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            FieldInfo field = type.GetField(name, flags);
            return field == null ? null : field.GetValue(instance);
        }

        private void LogExceptionThrottled(Exception exception)
        {
            if (Plugin.Log == null || Time.unscaledTime < _nextErrorLogTime)
                return;

            _nextErrorLogTime = Time.unscaledTime + 5f;
            Plugin.Log.LogWarning(
                "Camera synchronization failed and its baseline was reset: " +
                exception);
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" +
                   value.x.ToString("F3") + ", " +
                   value.y.ToString("F3") + ", " +
                   value.z.ToString("F3") + ")";
        }
    }
}
