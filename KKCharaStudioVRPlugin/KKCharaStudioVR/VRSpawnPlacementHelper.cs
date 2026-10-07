using System;
using System.Collections;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    public class VRSpawnPlacementHelper : MonoBehaviour
    {
        private static VRSpawnPlacementHelper _instance;
        public static VRSpawnPlacementHelper Instance => _instance;

        public static void Install(GameObject container)
        {
            if (_instance == null)
            {
                _instance = container.AddComponent<VRSpawnPlacementHelper>();
            }
        }

        private void Awake()
        {
            if (_instance == null)
                _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>
        /// Calculates a placement directly in front of the VR player's headset.
        /// </summary>
        public static bool CalculateFrontPlacement(
            float distance,
            bool isItem,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (VR.Camera == null || VR.Camera.Head == null)
                return false;

            Transform head = VR.Camera.Head;
            Vector3 headPos = head.position;

            // 1. Calculate horizontal forward direction
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;
            }
            forward.Normalize();

            // 2. Horizontal position in front of head
            Vector3 targetPos = headPos + forward * distance;

            // 3. Vertical position
            if (isItem)
            {
                // Items/props appear around waist/chest height in front of player
                targetPos.y = headPos.y - 0.35f;
            }
            else
            {
                // Characters should stand on the ground/floor
                float floorY;
                RaycastHit hit;
                // Exclude Chara and UI layers from ground raycast
                int mask = ~((1 << LayerMask.NameToLayer("Chara")) | (1 << LayerMask.NameToLayer("UI")));
                if (Physics.Raycast(targetPos + Vector3.up * 1.0f, Vector3.down, out hit, 6.0f, mask))
                {
                    floorY = hit.point.y;
                }
                else
                {
                    // Fall back to room-scale tracking floor
                    float eyeHeight = head.localPosition.y > 0.4f ? head.localPosition.y : 1.55f;
                    floorY = headPos.y - eyeHeight;
                }
                targetPos.y = floorY;
            }

            // 4. Rotation: face toward player
            Vector3 toPlayer = -forward;
            Quaternion targetRot = Quaternion.LookRotation(toPlayer, Vector3.up);

            position = targetPos;
            rotation = targetRot;
            return true;
        }

        /// <summary>
        /// Applies the position and rotation to the Studio ObjectCtrlInfo.
        /// </summary>
        public static void ApplyPlacement(ObjectCtrlInfo oci, Vector3 position, Quaternion rotation)
        {
            if (oci == null || oci.guideObject == null)
                return;

            GuideObject go = oci.guideObject;
            if (go.transformTarget == null)
                return;

            go.transformTarget.position = position;
            go.transformTarget.rotation = rotation;

            if (go.changeAmount != null)
            {
                go.changeAmount.pos = go.transformTarget.localPosition;
                go.changeAmount.rot = go.transformTarget.localEulerAngles;
            }

            go.SetEnable();

            if (oci is OCIChar ociChar)
            {
                if (ociChar.charInfo != null && ociChar.charInfo.animBody != null)
                {
                    ociChar.charInfo.animBody.Update(0f);
                }
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Disabled: Characters spawn at Studio's native default coordinates (world origin 0,0,0) to preserve VMD camera and dance motion alignment.
        /// </summary>
        public static void OnCharacterSpawned(OCIChar oci)
        {
            // Intentionally disabled: do not move newly spawned characters.
            // Preserves game native default coordinates (world origin) for VMD alignment.
        }

        /// <summary>
        /// Disabled: Items spawn at Studio's native default coordinates to preserve scene/VMD alignment.
        /// </summary>
        public static void OnItemSpawned(OCIItem ociItem)
        {
            // Intentionally disabled: do not move newly spawned items.
            // Preserves game native default coordinates (world origin) for VMD alignment.
        }

        private static IEnumerator VerifyPlacementCo(ObjectCtrlInfo oci, Vector3 targetPos, Quaternion targetRot)
        {
            yield return null;
            if (oci != null && oci.guideObject != null && oci.guideObject.transformTarget != null)
            {
                if (Vector3.Distance(oci.guideObject.transformTarget.position, targetPos) > 0.05f)
                {
                    ApplyPlacement(oci, targetPos, targetRot);
                }
            }
        }

        /// <summary>
        /// Brings the currently selected character (or first character) to the player's front.
        /// </summary>
        public static bool CallSelectedCharacter(bool keepY, out string feedback)
        {
            feedback = "";
            if (VR.Camera == null || VR.Camera.Head == null)
            {
                feedback = "VR 头显尚未就绪";
                return false;
            }

            Studio.Studio studio = Singleton<Studio.Studio>.Instance;
            if (studio == null)
            {
                feedback = "工作室尚未就绪";
                return false;
            }

            ObjectCtrlInfo target = null;
            if (studio.treeNodeCtrl != null && studio.treeNodeCtrl.selectObjectCtrl != null && studio.treeNodeCtrl.selectObjectCtrl.Length > 0)
            {
                target = studio.treeNodeCtrl.selectObjectCtrl[0];
            }

            if (target == null && studio.dicInfo != null)
            {
                foreach (var kvp in studio.dicInfo)
                {
                    if (kvp.Value is OCIChar)
                    {
                        target = kvp.Value;
                        break;
                    }
                }
            }

            if (target == null)
            {
                feedback = "场景中没有可召唤的角色";
                return false;
            }

            float distance = 1.5f;
            KKCharaStudioVRSettings settings = VR.Manager?.Context?.Settings as KKCharaStudioVRSettings;
            if (settings != null)
            {
                distance = settings.CharSpawnDistance;
            }

            if (CalculateFrontPlacement(distance, target is OCIItem, out Vector3 pos, out Quaternion rot))
            {
                if (keepY && target.guideObject != null && target.guideObject.transformTarget != null)
                {
                    pos.y = target.guideObject.transformTarget.position.y;
                }

                if (target.guideObject != null && target.guideObject.transformTarget != null)
                {
                    Vector3 oldPos = target.guideObject.transformTarget.localPosition;
                    ApplyPlacement(target, pos, rot);

                    if (target.guideObject.enablePos)
                    {
                        Singleton<UndoRedoManager>.Instance.Push((ICommand)new GuideCommand.MoveEqualsCommand(new GuideCommand.EqualsInfo[]
                        {
                            new GuideCommand.EqualsInfo
                            {
                                dicKey = target.guideObject.dicKey,
                                oldValue = oldPos,
                                newValue = target.guideObject.changeAmount.pos
                            }
                        }));
                    }
                }
                else
                {
                    ApplyPlacement(target, pos, rot);
                }

                string name = target.treeNodeObject != null ? target.treeNodeObject.textName : "角色";
                feedback = $"已将【{name}】召唤至眼前";
                VRLog.Info($"[VRSpawnPlacementHelper] {feedback}");
                return true;
            }

            feedback = "召唤失败：无法计算前方坐标";
            return false;
        }

        /// <summary>
        /// Brings all characters in the scene to the player's front, spaced out horizontally.
        /// </summary>
        public static int CallAllCharacters(bool keepY, out string feedback)
        {
            feedback = "";
            if (VR.Camera == null || VR.Camera.Head == null)
            {
                feedback = "VR 头显尚未就绪";
                return 0;
            }

            Studio.Studio studio = Singleton<Studio.Studio>.Instance;
            if (studio == null || studio.dicInfo == null)
            {
                feedback = "工作室尚未就绪";
                return 0;
            }

            List<OCIChar> characters = new List<OCIChar>();
            foreach (var kvp in studio.dicInfo)
            {
                if (kvp.Value is OCIChar ociChar && !characters.Contains(ociChar))
                {
                    characters.Add(ociChar);
                }
            }

            if (characters.Count == 0)
            {
                feedback = "场景中没有角色";
                return 0;
            }

            float distance = 1.5f;
            KKCharaStudioVRSettings settings = VR.Manager?.Context?.Settings as KKCharaStudioVRSettings;
            if (settings != null)
            {
                distance = settings.CharSpawnDistance;
            }

            Transform head = VR.Camera.Head;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            }
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            float spacing = 0.8f;
            int count = characters.Count;

            for (int i = 0; i < count; i++)
            {
                float lateralOffset = (i - (count - 1) * 0.5f) * spacing;
                Vector3 basePos = head.position + forward * distance + right * lateralOffset;

                float floorY;
                RaycastHit hit;
                int mask = ~((1 << LayerMask.NameToLayer("Chara")) | (1 << LayerMask.NameToLayer("UI")));
                if (Physics.Raycast(basePos + Vector3.up * 1.0f, Vector3.down, out hit, 6.0f, mask))
                {
                    floorY = hit.point.y;
                }
                else
                {
                    float eyeHeight = head.localPosition.y > 0.4f ? head.localPosition.y : 1.55f;
                    floorY = head.position.y - eyeHeight;
                }

                if (keepY && characters[i].guideObject != null && characters[i].guideObject.transformTarget != null)
                {
                    basePos.y = characters[i].guideObject.transformTarget.position.y;
                }
                else
                {
                    basePos.y = floorY;
                }

                Quaternion rot = Quaternion.LookRotation(-forward, Vector3.up);
                ApplyPlacement(characters[i], basePos, rot);
            }

            feedback = $"已将全部 {count} 名角色召唤至眼前";
            VRLog.Info($"[VRSpawnPlacementHelper] {feedback}");
            return count;
        }
    }
}
