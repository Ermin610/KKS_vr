using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// VRBoop: Attaches VR hand & fingertip DynamicBoneCollider proxies to all character DynamicBones
    /// (breasts, buttocks, skirts, hair, accessories) so physical hand contact pushes and kneads them smoothly.
    /// </summary>
    public static class VRBoop
    {
        private static readonly List<DynamicBoneCollider> _activeDBC = new List<DynamicBoneCollider>();
        private static bool _initialized;

        public static IReadOnlyList<DynamicBoneCollider> ActiveColliders => _activeDBC;

        public static void Initialize()
        {
            if (_initialized) return;
            try
            {
                Harmony.CreateAndPatchAll(typeof(VRBoop), "KKCharaStudioVR.VRBoop");
                _initialized = true;
                VRLog.Info("[VRBoop] DynamicBone kneading patches successfully installed.");
            }
            catch (Exception ex)
            {
                VRLog.Warn("[VRBoop] Failed to patch DynamicBone: " + ex.Message);
            }
        }

        public static void AddDB(DynamicBoneCollider dbCollider)
        {
            if (dbCollider == null) return;
            if (!_activeDBC.Contains(dbCollider))
            {
                _activeDBC.Add(dbCollider);
            }
        }

        public static void RemoveDB(DynamicBoneCollider dbCollider)
        {
            if (dbCollider != null)
            {
                _activeDBC.Remove(dbCollider);
            }
        }

        public static void RefreshDynamicBones(IEnumerable<ChaControl> charas)
        {
            if (charas == null) return;

            foreach (ChaControl chara in charas)
            {
                if (chara == null) continue;

                DynamicBone[] dbArr1 = chara.GetComponentsInChildren<DynamicBone>(true);
                for (int i = 0; i < dbArr1.Length; i++)
                {
                    AttachControllerColliders(dbArr1[i]);
                }

                DynamicBone_Ver01[] dbArr2 = chara.GetComponentsInChildren<DynamicBone_Ver01>(true);
                for (int i = 0; i < dbArr2.Length; i++)
                {
                    AttachControllerColliders(dbArr2[i]);
                }

                DynamicBone_Ver02[] dbArr3 = chara.GetComponentsInChildren<DynamicBone_Ver02>(true);
                for (int i = 0; i < dbArr3.Length; i++)
                {
                    AttachControllerColliders(dbArr3[i]);
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyWrapSafe]
        [HarmonyPatch(typeof(DynamicBone), "SetupParticles")]
        [HarmonyPatch(typeof(DynamicBone_Ver01), "SetupParticles")]
        [HarmonyPatch(typeof(DynamicBone_Ver02), "SetupParticles")]
        private static void OnDynamicBoneInit(MonoBehaviour __instance)
        {
            AttachControllerColliders(__instance);
        }

        [HarmonyPrefix]
        [HarmonyWrapSafe]
        [HarmonyPatch(typeof(ChaControl), "LoadCharaFbxDataAsync")]
        private static void OnClothesChanged(ref Action<GameObject> actObj)
        {
            actObj = (Action<GameObject>)Delegate.Combine(actObj, (Action<GameObject>)delegate (GameObject newObj)
            {
                if (newObj != null)
                {
                    DynamicBone[] db1 = newObj.GetComponentsInChildren<DynamicBone>(true);
                    for (int i = 0; i < db1.Length; i++)
                    {
                        if (db1[i].m_Colliders != null) AddColliders(db1[i].m_Colliders);
                    }

                    DynamicBone_Ver01[] db2 = newObj.GetComponentsInChildren<DynamicBone_Ver01>(true);
                    for (int i = 0; i < db2.Length; i++)
                    {
                        if (db2[i].m_Colliders != null) AddColliders(db2[i].m_Colliders);
                    }

                    DynamicBone_Ver02[] db3 = newObj.GetComponentsInChildren<DynamicBone_Ver02>(true);
                    for (int i = 0; i < db3.Length; i++)
                    {
                        if (db3[i].Colliders != null) AddColliders(db3[i].Colliders);
                    }
                }
            });
        }

        public static void AttachControllerColliders(MonoBehaviour dynamicBone)
        {
            if (dynamicBone == null) return;
            List<DynamicBoneCollider> colliderList = GetColliderList(dynamicBone);
            if (colliderList != null)
            {
                AddColliders(colliderList);
            }
        }

        private static void AddColliders(List<DynamicBoneCollider> colliderList)
        {
            if (colliderList == null) return;
            for (int i = 0; i < _activeDBC.Count; i++)
            {
                DynamicBoneCollider item = _activeDBC[i];
                if (item != null && !colliderList.Contains(item))
                {
                    colliderList.Add(item);
                }
            }
        }

        private static List<DynamicBoneCollider> GetColliderList(MonoBehaviour dynamicBone)
        {
            if (dynamicBone is DynamicBone db)
            {
                if (db.m_Colliders == null) db.m_Colliders = new List<DynamicBoneCollider>();
                return db.m_Colliders;
            }

            if (dynamicBone is DynamicBone_Ver01 db1)
            {
                if (db1.m_Colliders == null) db1.m_Colliders = new List<DynamicBoneCollider>();
                return db1.m_Colliders;
            }

            if (dynamicBone is DynamicBone_Ver02 db2)
            {
                if (db2.Colliders == null) db2.Colliders = new List<DynamicBoneCollider>();
                return db2.Colliders;
            }

            return null;
        }
    }
}
