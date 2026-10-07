using System;
using UnityEngine;

namespace KKCharaStudioVR
{
    /// <summary>
    /// Proxy component attached to hand and finger colliders (Palm, Thumb, Index, Middle, Ring, Little)
    /// to forward 3D trigger contact events and contact coordinates to the hand's VRPhysicalTouchHandler.
    /// </summary>
    public class VRHandContactProxy : MonoBehaviour
    {
        public VRPhysicalTouchHandler touchHandler;
        public bool isLeftHand;
        public int fingerIndex = -1; // -1 for palm, 0~4 for fingers

        private void OnTriggerEnter(Collider other)
        {
            if (touchHandler == null) return;
            Vector3 contactPos = other.ClosestPoint(transform.position);
            touchHandler.ProcessContact(other, isInitialContact: true, contactPos);
        }

        private void OnTriggerStay(Collider other)
        {
            if (touchHandler == null) return;
            Vector3 contactPos = other.ClosestPoint(transform.position);
            touchHandler.ProcessContact(other, isInitialContact: false, contactPos);
        }

        private void OnTriggerExit(Collider other)
        {
            if (touchHandler == null) return;
            touchHandler.ProcessContactExit(other);
        }
    }
}
