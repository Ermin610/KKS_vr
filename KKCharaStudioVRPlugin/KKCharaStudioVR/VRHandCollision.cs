using System.Collections.Generic;
using UnityEngine;
using VRGIN.Core;
using VRGIN.Visuals;

namespace KKCharaStudioVR
{
    /// <summary>
    /// Unity colliders on the VR hands must not shove the studio UI or the
    /// camera rig. DynamicBoneCollider components are not Unity colliders;
    /// breasts, hair, and skirts keep using those proxies.
    /// </summary>
    internal static class VRHandCollision
    {
        private static readonly List<Collider> Proxies = new List<Collider>();
        private static readonly List<Collider> Blockers = new List<Collider>();
        private static bool _matrixReady;
        private static bool _logged;
        private static VRHandCollisionDriver _driver;

        public static void AdoptProxy(GameObject obj)
        {
            if (obj == null) return;
            InstallMatrix();
            Collider[] cols = obj.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                AdoptCollider(cols[i]);
        }

        public static void Refresh()
        {
            InstallMatrix();
            EnsureDriver();
            Prune(Proxies);
            if (Proxies.Count == 0) return;
            CollectBlockers();
            ApplyIgnore();
            if (!_logged)
            {
                _logged = true;
                VRLog.Info("Hand colliders ignore UI and camera. DynamicBone colliders stay active.");
            }
        }

        /// <summary>
        /// Ignore pairs are cleared when either collider is disabled. Reapply
        /// them every physics step so a hand that just woke up cannot shove
        /// the UI or the headset for a frame.
        /// </summary>
        public static void ApplyIgnore()
        {
            Prune(Proxies);
            Prune(Blockers);
            for (int i = 0; i < Proxies.Count; i++)
            {
                Collider hand = Proxies[i];
                if (hand == null) continue;
                for (int j = 0; j < Blockers.Count; j++)
                {
                    Collider block = Blockers[j];
                    if (block == null || block == hand) continue;
                    Physics.IgnoreCollision(hand, block, true);
                }
            }
        }

        public static bool ShouldIgnore(Collider other)
        {
            if (other == null) return false;
            int layer = other.gameObject.layer;
            int ui = LayerMask.NameToLayer("UI");
            int select = LayerMask.NameToLayer("Studio/Select");
            if (ui >= 0 && layer == ui) return true;
            if (select >= 0 && layer == select) return true;
            Transform t = other.transform;
            if (t.GetComponentInParent<GUIQuad>() != null) return true;
            if (t.GetComponentInParent<Canvas>() != null) return true;
            if (t.GetComponentInParent<Camera>() != null) return true;
            Rigidbody body = other.attachedRigidbody;
            if (body != null)
            {
                if (((Component)body).GetComponent<Camera>() != null) return true;
                if (((Component)body).GetComponent<GUIQuad>() != null) return true;
            }
            return false;
        }

        private static void AdoptCollider(Collider col)
        {
            if (col == null) return;
            GameObject go = col.gameObject;
            // The VR camera drops InvisibleLayer ("Ignore Raycast") from its
            // culling mask. Only collider-only objects can use that layer;
            // a renderer on the same object would disappear.
            if (go.GetComponent<Renderer>() == null)
            {
                int layer = LayerMask.NameToLayer("Ignore Raycast");
                if (layer >= 0)
                    go.layer = layer;
            }
            if (go.GetComponent<VRHandContactFilter>() == null)
                go.AddComponent<VRHandContactFilter>();
            if (!Proxies.Contains(col))
                Proxies.Add(col);
        }

        private static void EnsureDriver()
        {
            if (_driver != null) return;
            GameObject go = new GameObject("VRHandCollisionDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<VRHandCollisionDriver>();
        }

        private static void InstallMatrix()
        {
            if (_matrixReady) return;
            int hand = LayerMask.NameToLayer("Ignore Raycast");
            if (hand < 0) return;
            int ui = LayerMask.NameToLayer("UI");
            int select = LayerMask.NameToLayer("Studio/Select");
            if (ui >= 0)
                Physics.IgnoreLayerCollision(hand, ui, true);
            if (select >= 0)
                Physics.IgnoreLayerCollision(hand, select, true);
            _matrixReady = true;
        }

        private static void CollectBlockers()
        {
            Blockers.Clear();
            GUIQuad[] quads = Object.FindObjectsOfType<GUIQuad>();
            for (int i = 0; i < quads.Length; i++)
            {
                if (quads[i] == null) continue;
                Collider[] cols = quads[i].GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                    AddBlocker(cols[c]);
            }
            AddCameraColliders(Camera.main);
            if (VR.Camera != null)
            {
                AddCameraColliders(((Component)VR.Camera).GetComponent<Camera>());
                if (VR.Camera.SteamCam != null)
                {
                    if (VR.Camera.SteamCam.head != null)
                    {
                        // Head only. SteamCam.origin parents the whole rig; scanning
                        // its children would ignore collisions with the world.
                        AddRigColliders(VR.Camera.SteamCam.head);
                    }
                    if (VR.Camera.SteamCam.origin != null)
                    {
                        Collider[] originCols = VR.Camera.SteamCam.origin.GetComponents<Collider>();
                        for (int i = 0; i < originCols.Length; i++)
                            AddBlocker(originCols[i]);
                    }
                }
            }
        }

        private static void AddRigColliders(Transform root)
        {
            if (root == null) return;
            Collider[] cols = root.GetComponentsInChildren<Collider>(true);
            // A camera that parents the studio scene would otherwise put the
            // whole map on the ignore list and the hands would fall through it.
            if (cols.Length > 48)
                cols = root.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
                AddBlocker(cols[i]);
        }

        private static void AddBlocker(Collider col)
        {
            if (col == null || Blockers.Contains(col)) return;
            if (col.GetComponent<VRHandContactFilter>() != null) return;
            Blockers.Add(col);
        }

        private static void AddCameraColliders(Camera cam)
        {
            if (cam == null) return;
            AddRigColliders(((Component)cam).transform);
        }

        private static void Prune(List<Collider> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null)
                    list.RemoveAt(i);
            }
        }
    }

    internal sealed class VRHandContactFilter : MonoBehaviour
    {
        private Collider _self;
        private readonly Vector3[] _normals = new Vector3[4];
        private int _normalCount;

        private void Awake()
        {
            _self = GetComponent<Collider>();
        }

        private void OnEnable()
        {
            VRHandCollision.ApplyIgnore();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null) return;
            if (Ignore(collision.collider)) return;
            Remember(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (collision == null) return;
            if (Ignore(collision.collider)) return;
            Remember(collision);
        }

        private void OnTriggerEnter(Collider other)
        {
            Ignore(other);
        }

        internal int CopyAndClearNormals(Vector3[] buffer, int offset)
        {
            if (buffer == null) return 0;
            int written = 0;
            int count = _normalCount;
            _normalCount = 0;
            for (int i = 0; i < count && offset + written < buffer.Length; i++)
            {
                buffer[offset + written] = _normals[i];
                written++;
            }
            return written;
        }

        private void Remember(Collision collision)
        {
            int contacts = collision.contactCount;
            for (int i = 0; i < contacts && _normalCount < _normals.Length; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                if (normal.sqrMagnitude < 0.0001f) continue;
                _normals[_normalCount++] = normal;
            }
        }

        private bool Ignore(Collider other)
        {
            if (_self == null || other == null) return false;
            if (!VRHandCollision.ShouldIgnore(other)) return false;
            Physics.IgnoreCollision(_self, other, true);
            return true;
        }
    }

    [DefaultExecutionOrder(-10000)]
    internal sealed class VRHandCollisionDriver : MonoBehaviour
    {
        private float _nextCollect;

        private void FixedUpdate()
        {
            if (Time.unscaledTime >= _nextCollect)
            {
                _nextCollect = Time.unscaledTime + 0.25f;
                VRHandCollision.Refresh();
                return;
            }
            VRHandCollision.ApplyIgnore();
        }
    }
}
