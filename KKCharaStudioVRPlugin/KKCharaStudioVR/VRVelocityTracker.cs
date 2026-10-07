using System;
using UnityEngine;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// 6-DOF Real-time kinematic velocity tracker for VR hand controllers.
    /// Tracks both Linear Velocity (v) and Angular Velocity (omega).
    /// Computes point tangential velocity at any contact point (palm, fingertips):
    ///   v_point = v_linear + omega x (p_point - p_controller)
    /// This accurately captures wrist-flicking, rotational slaps, and gentle sliding motions.
    /// </summary>
    public class VRVelocityTracker : MonoBehaviour
    {
        public SteamVR_TrackedObject trackedObject;

        private Vector3 _linearVelocity;
        private Vector3 _angularVelocity;
        private Vector3 _prevPosition;
        private Quaternion _prevRotation;
        private bool _hasPrevPose;
        private float _lastUpdateTime;

        public Vector3 LinearVelocity => _linearVelocity;
        public Vector3 AngularVelocity => _angularVelocity;
        public float LinearSpeed => _linearVelocity.magnitude;
        public float AngularSpeed => _angularVelocity.magnitude;

        private void Start()
        {
            if (trackedObject == null)
            {
                trackedObject = GetComponentInParent<SteamVR_TrackedObject>();
            }
            _prevPosition = transform.position;
            _prevRotation = transform.rotation;
            _hasPrevPose = true;
            _lastUpdateTime = Time.unscaledTime;
        }

        private void Update()
        {
            UpdateVelocity();
        }

        private void FixedUpdate()
        {
            UpdateVelocity();
        }

        public void UpdateVelocity()
        {
            float now = Time.unscaledTime;
            float dt = now - _lastUpdateTime;
            if (dt < 0.0005f) return;
            _lastUpdateTime = now;

            Vector3 currentPos = transform.position;
            Quaternion currentRot = transform.rotation;

            bool hardwareVelocityObtained = false;

            // 1. Try reading hardware-reported velocity from SteamVR device
            if (trackedObject != null && trackedObject.index != SteamVR_TrackedObject.EIndex.None)
            {
                var device = SteamVR_Controller.Input((int)trackedObject.index);
                if (device != null && device.hasTracking)
                {
                    Transform origin = null;
                    if (VR.Camera != null && VR.Camera.SteamCam != null && VR.Camera.SteamCam.origin != null)
                    {
                        origin = VR.Camera.SteamCam.origin;
                    }
                    else if (transform.parent != null)
                    {
                        origin = transform.parent;
                    }

                    // Hardware velocity query via reflection if available on specific SteamVR build
                    try
                    {
                        var velProp = device.GetType().GetProperty("velocity");
                        var angProp = device.GetType().GetProperty("angularVelocity");
                        if (velProp != null && angProp != null)
                        {
                            Vector3 hwLin = (Vector3)velProp.GetValue(device, null);
                            Vector3 hwAng = (Vector3)angProp.GetValue(device, null);
                            if (origin != null)
                            {
                                hwLin = origin.TransformVector(hwLin);
                                hwAng = origin.TransformVector(hwAng);
                            }
                            if (IsFinite(hwLin) && IsFinite(hwAng))
                            {
                                _linearVelocity = Vector3.Lerp(_linearVelocity, hwLin, 0.6f);
                                _angularVelocity = Vector3.Lerp(_angularVelocity, hwAng, 0.6f);
                                hardwareVelocityObtained = true;
                            }
                        }
                    }
                    catch { }
                }
            }

            // 2. Numerical differentiation fallback / complementary filter
            if (!hardwareVelocityObtained && _hasPrevPose)
            {
                if (dt > 0.0001f && dt < 0.2f)
                {
                    // Linear velocity
                    Vector3 instantLin = (currentPos - _prevPosition) / dt;
                    if (IsFinite(instantLin))
                    {
                        _linearVelocity = Vector3.Lerp(_linearVelocity, instantLin, 0.5f);
                    }

                    // Angular velocity from quaternion delta
                    Quaternion deltaRot = currentRot * Quaternion.Inverse(_prevRotation);
                    deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f) angleDeg -= 360f;

                    if (IsFinite(axis) && !float.IsNaN(angleDeg))
                    {
                        Vector3 instantAng = axis * (angleDeg * Mathf.Deg2Rad / dt);
                        if (IsFinite(instantAng))
                        {
                            _angularVelocity = Vector3.Lerp(_angularVelocity, instantAng, 0.5f);
                        }
                    }
                }
            }

            _prevPosition = currentPos;
            _prevRotation = currentRot;
            _hasPrevPose = true;
        }

        /// <summary>
        /// Computes 3D world velocity of a specific point (e.g. fingertip, palm center) on the hand,
        /// combining linear translation and angular tangential velocity (v_point = v_lin + omega x r).
        /// </summary>
        public Vector3 GetPointVelocity(Vector3 worldPoint)
        {
            Vector3 r = worldPoint - transform.position;
            Vector3 tangential = Vector3.Cross(_angularVelocity, r);
            return _linearVelocity + tangential;
        }

        /// <summary>
        /// Computes effective speed (magnitude of point velocity) at the given contact point.
        /// </summary>
        public float GetPointSpeed(Vector3 worldPoint)
        {
            return GetPointVelocity(worldPoint).magnitude;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
