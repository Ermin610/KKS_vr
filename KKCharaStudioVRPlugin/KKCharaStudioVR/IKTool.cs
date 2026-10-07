using System;
using System.Collections;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRGIN.Core;
using Object = UnityEngine.Object;

namespace KKCharaStudioVR;

public class IKTool : MonoBehaviour
{
	private bool markerShowOverlay = true;

	private float markerOverlayAlpha = 0.4f;

	private float markerSize = 0.06f;

	private float markerSizeBend = 0.05f;

	private Material markerSharedMaterial;

	private Material markerSharedMaterial_IKTarget;

	private Material markerSharedMaterial_IKBendTarget;

	private GameObject handle;

	public static IKTool instance;

	private const float DEFAUTL_SCALE_POS = 0.25f;

	private float DEFAULT_SCALE_POS_XYZ_DIST = Mathf.Sqrt(0.1875f);

	public static IKTool Create(GameObject container)
	{
		if (instance != null)
		{
			return instance;
		}
		instance = container.AddComponent<IKTool>();
		return instance;
	}

	private void Awake()
	{
	}

	private void Start()
	{
		StartWatch();
	}

	private bool _missingSphereLogged;
	private bool _installFailureLogged;

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		StartWatch();
	}

	private IEnumerator InstallMoveableObjectCo()
	{
		_ = Singleton<GuideObjectManager>.Instance;
		while (true)
		{
			yield return (object)new WaitForSeconds(1f);
			Studio.Studio studio = Singleton<Studio.Studio>.Instance;
			if (studio == null || studio.dicObjectCtrl == null)
				continue;
			try
			{
				var enumerator = studio.dicObjectCtrl.GetEnumerator();
				while (enumerator.MoveNext())
				{
					var item = enumerator.Current;
					int key = item.Key;
					ObjectCtrlInfo value = item.Value;

					// Do not remember the character key. IK and FK lists are often
					// still empty the first time a character is seen; marking it
					// done then leaves every bone without a grab marker.
					if (value == null || value.guideObject == null || ((Component)value.guideObject).gameObject == null)
						continue;

					try
					{
						MakeObjectMoveable(value.guideObject, replaceMaterial: true, installToCenter: true);
						if (value is OCIChar val)
						{
							if (val.listIKTarget != null)
							{
								foreach (OCIChar.IKInfo item2 in val.listIKTarget)
								{
									if (item2 != null && item2.guideObject != null)
										MakeObjectMoveable(item2.guideObject, replaceMaterial: true);
								}
							}

							if (val.listBones != null)
							{
								foreach (OCIChar.BoneInfo listBone in val.listBones)
								{
									if (listBone != null && listBone.guideObject != null)
										MakeObjectMoveable(listBone.guideObject, replaceMaterial: true);
								}
							}
						}
					}
					catch (Exception ex)
					{
						if (!_installFailureLogged)
						{
							_installFailureLogged = true;
							VRLog.Warn("IK grab marker install failed for object " + key + " and will retry: " + ex);
						}
					}
				}
				enumerator.Dispose();
			}
			catch (Exception value2)
			{
				if (!_installFailureLogged)
				{
					_installFailureLogged = true;
					VRLog.Warn("IK grab marker scan failed: " + value2);
				}
			}
		}
	}

	private void StartWatch()
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Expected O, but got Unknown
		((MonoBehaviour)this).StopAllCoroutines();
		((MonoBehaviour)this).StartCoroutine(InstallMoveableObjectCo());
		if (handle == null)
		{
			handle = new GameObject("handle");
			handle.transform.parent = ((Component)this).gameObject.transform;
		}
	}

	private void MakeObjectMoveable(GuideObject guideObject, bool replaceMaterial = false, bool installToCenter = false)
	{
		if (guideObject == null || guideObject.transformTarget == null)
			return;
		GameObject host = ((Component)guideObject).gameObject;
		if (!installToCenter)
		{
			Transform sphere = host.transform.Find("Sphere");
			if (sphere == null)
			{
				if (!_missingSphereLogged)
				{
					_missingSphereLogged = true;
					VRLog.Warn("IK guide '" + host.name + "' has no Sphere child. The grab marker is attached to the guide root.");
				}
			}
			else
			{
				host = ((Component)sphere).gameObject;
			}
		}
		InstallGripMoveMarker(host, OnObjectMove, guideObject, replaceMaterial, installToCenter);
		if (guideObject.enableScale)
			InstallScaleMoveMarker(guideObject);
	}

	private bool InstallGripMoveMarker(GameObject target, Action<MonoBehaviour> moveHandler, GuideObject guideObject, bool replaceMaterial, bool installToCenter)
	{
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d2: Expected O, but got Unknown
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected O, but got Unknown
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Expected O, but got Unknown
		Transform existingMarker = target.transform.Find("_gripmovemarker");
		if (existingMarker != null)
		{
			MoveableGUIObject existing = existingMarker.GetComponent<MoveableGUIObject>();
			if (existing != null && existing.guideObject == null)
				existing.guideObject = guideObject;
			return false;
		}
		{
			Renderer visibleReference = null;
			GameObject val;
			if (installToCenter)
			{
				val = GameObject.CreatePrimitive((PrimitiveType)0);
				((UnityEngine.Object)val).name = "_gripmovemarker";
				val.layer = LayerMask.NameToLayer("Studio/Select");
				Renderer component = val.GetComponent<Renderer>();
				Material val2 = new Material(MaterialHelper.GetColorZOrderShader());
				val2.color = new Color(0f, 1f, 0f, 0.3f);
				val2.SetFloat("_AlphaRatio", 1.5f);
				val2.renderQueue = 3800;
				component.material = val2;
				Transform val3 = target.transform.Find("move/XYZ");
				if (val3 != null)
				{
					visibleReference = ((Component)val3).gameObject.GetComponent<Renderer>();
				}
			}
			else
			{
				val = new GameObject("_gripmovemarker");
				if (replaceMaterial)
				{
					Renderer component2 = target.GetComponent<Renderer>();
					if (component2 != null)
					{
						Material val4 = new Material(MaterialHelper.GetColorZOrderShader());
						val4.CopyPropertiesFromMaterial(component2.material);
						val4.SetFloat("_AlphaRatio", 1.5f);
						val4.renderQueue = 3800;
						component2.material = val4;
						visibleReference = component2;
					}
				}
			}
			SphereCollider val5 = val.AddComponent<SphereCollider>();
			Transform transform = val.transform;
			((Component)transform).transform.parent = target.transform;
			((Component)transform).transform.localPosition = Vector3.zero;
			((Component)transform).transform.rotation = guideObject.transformTarget.rotation;
			((Component)transform).transform.localScale = Vector3.one;
			((Collider)val5).isTrigger = true;
			MoveableGUIObject moveableGUIObject = val.AddComponent<MoveableGUIObject>();
			moveableGUIObject.guideObject = guideObject;
			moveableGUIObject.onMoveLister.Add(moveHandler);
			moveableGUIObject.visibleReference = visibleReference;
			if (installToCenter)
			{
				moveableGUIObject.isMoveObj = true;
			}
			return true;
		}
	}

	private bool InstallScaleMoveMarker(GuideObject guideObject)
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Expected O, but got Unknown
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		Transform val = ((Component)guideObject).gameObject.transform.Find("scale");
		if (val != null && val.Find("X/_gripmovemarker_scale") == null)
		{
			string[] array = new string[4] { "XYZ", "X", "Y", "Z" };
			foreach (string text in array)
			{
				Transform val2 = val.Find(text);
				if (val2 == null)
					continue;
				GuideScale component = ((Component)val2).gameObject.GetComponent<GuideScale>();
				if (component != null)
				{
					GameObject obj = GameObject.CreatePrimitive((PrimitiveType)0);
					((UnityEngine.Object)obj).name = "_gripmovemarker_scale";
					obj.layer = LayerMask.NameToLayer("Studio/Select");
					Renderer component2 = obj.GetComponent<Renderer>();
					Material val3 = new Material(MaterialHelper.GetColorZOrderShader());
					val3.color = new Color(0f, 1f, 1f, 0.3f);
					val3.SetFloat("_AlphaRatio", 1.5f);
					val3.renderQueue = 3800;
					component2.material = val3;
					Renderer val4 = null;
					val4 = ((Component)val2).gameObject.GetComponent<Renderer>();
					SphereCollider val5 = obj.AddComponent<SphereCollider>();
					Transform transform = obj.transform;
					((Component)transform).transform.parent = val2;
					((Component)transform).transform.localPosition = CalcScaleHandleDefaultPos(component);
					((Component)transform).transform.rotation = guideObject.transformTarget.rotation;
					((Component)transform).transform.localScale = Vector3.one;
					((Collider)val5).isTrigger = true;
					MoveableGUIObject moveableGUIObject = obj.AddComponent<MoveableGUIObject>();
					moveableGUIObject.guideObject = guideObject;
					moveableGUIObject.guideScale = component;
					moveableGUIObject.onMoveLister.Add(OnScaleMove);
					moveableGUIObject.onReleasedLister.Add(OnScaleReleased);
					moveableGUIObject.visibleReference = val4;
				}
			}
			return true;
		}
		return false;
	}

	private void OnObjectMove(MonoBehaviour marker)
	{
		DoOnMove(marker, ((Component)marker).transform.parent);
	}

	private void OnObjectCubeMoveNoRotation(MonoBehaviour marker)
	{
		DoOnMove(marker, ((Component)marker).transform.parent.parent, rotation: false);
	}

	private void OnObjectRotationNoMove(MonoBehaviour marker)
	{
		DoOnMove(marker, ((Component)marker).transform.parent, rotation: true, pos: false);
	}

	private void OnRawObjectMove(MonoBehaviour marker)
	{
		DoOnMove(marker, ((Component)marker).transform.parent);
	}

	private void DoOnMove(MonoBehaviour marker, Transform target, bool rotation = true, bool pos = true)
	{
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		if (marker == null) return;
		MoveableGUIObject component = ((Component)marker).GetComponent<MoveableGUIObject>();
		if (component == null || component.guideObject == null) return;
		Transform parent = ((Component)marker).transform.parent;
		GuideObject guideObject = component.guideObject;
		Transform bone = guideObject.transformTarget;
		pos &= guideObject.enablePos;
		rotation &= guideObject.enableRot;
		// Without an active IK/FK chain the Animator re-evaluates the limb and the
		// bone snaps back to the clip on the very next frame.
		VRIkPosing.EnsureChainActive(guideObject);
		if (pos && bone != null && parent != null && IsFinite(((Component)marker).transform.position))
		{
			// The guide sphere is sometimes parented under the bone. Moving that
			// parent and then assigning the bone applies the same delta twice
			// and the limb jumps.
			bool coupled = parent == bone || parent.IsChildOf(bone);
			Vector3 desired = ((Component)marker).transform.position;
			if (!coupled)
			{
				Vector3 delta = ((Component)marker).transform.position - parent.position;
				if (IsFinite(delta))
				{
					target.position += delta;
					desired = target.position;
				}
			}
			if (IsFinite(desired))
			{
				Vector3 backup = bone.position;
				bone.position = desired;
				Vector3 local = bone.localPosition;
				if (IsFinite(local) && local.sqrMagnitude < 10000f)
					guideObject.changeAmount.pos = local;
				else
					bone.position = backup;
			}
		}
		if (parent != null)
			((Component)marker).transform.localPosition = Vector3.zero;
		if (rotation && bone != null && IsFinite(((Component)marker).transform.rotation))
		{
			bone.rotation = ((Component)marker).transform.rotation;
			Vector3 euler = bone.localEulerAngles;
			if (IsFinite(euler))
			{
				// localEulerAngles is 0..360. Writing that straight into
				// changeAmount.rot spins the bone a full turn whenever the
				// stored angle was negative.
				guideObject.changeAmount.rot = UnwrapEuler(guideObject.changeAmount.rot, euler);
			}
		}
		if (pos || rotation)
			VRIkPosing.Commit(guideObject);
	}

	private static bool IsFinite(Vector3 value)
	{
		return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
			&& !float.IsNaN(value.y) && !float.IsInfinity(value.y)
			&& !float.IsNaN(value.z) && !float.IsInfinity(value.z);
	}

	private static bool IsFinite(Quaternion value)
	{
		if (!IsFinite(new Vector3(value.x, value.y, value.z))
			|| float.IsNaN(value.w) || float.IsInfinity(value.w))
			return false;
		float mag = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
		return mag > 0.0001f && mag < 4f;
	}

	private static Vector3 UnwrapEuler(Vector3 previous, Vector3 current)
	{
		if (!IsFinite(previous)) return current;
		current.x = previous.x + Mathf.DeltaAngle(previous.x, current.x);
		current.y = previous.y + Mathf.DeltaAngle(previous.y, current.y);
		current.z = previous.z + Mathf.DeltaAngle(previous.z, current.z);
		return current;
	}

	private void OnScaleMove(MonoBehaviour marker)
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Expected I4, but got Unknown
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0120: Unknown result type (might be due to invalid IL or missing references)
		MoveableGUIObject component = ((Component)marker).GetComponent<MoveableGUIObject>();
		_ = ((Component)marker).transform.parent;
		GuideObject guideObject = component.guideObject;
		GuideScale guideScale = component.guideScale;
		if (!guideObject.enableScale || (component.guideScale == null))
		{
			return;
		}
		Vector3 localPosition = ((Component)marker).transform.localPosition;
		float magnitude = localPosition.magnitude;
		if (magnitude > 0f)
		{
			float num = magnitude / 0.25f;
			Vector3 val = component.oldScale;
			GuideScale.ScaleAxis axis = guideScale.axis;
			switch ((int)axis)
			{
			case 3:
				val *= num;
				break;
			case 0:
				val.x *= num;
				break;
			case 1:
				val.y *= num;
				break;
			case 2:
				val.z *= num;
				break;
			}
			val.x = Mathf.Max(val.x, 0.01f);
			val.y = Mathf.Max(val.y, 0.01f);
			val.z = Mathf.Max(val.z, 0.01f);
			guideObject.changeAmount.scale = val;
		}
	}

	private void OnScaleReleased(MonoBehaviour marker)
	{
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		MoveableGUIObject component = ((Component)marker).GetComponent<MoveableGUIObject>();
		GuideObject guideObject = component.guideObject;
		GuideScale guideScale = component.guideScale;
		if (guideObject.enableScale && (guideScale != null))
		{
			((Component)marker).transform.localPosition = CalcScaleHandleDefaultPos(guideScale);
		}
	}

	private Vector3 CalcScaleHandleDefaultPos(GuideScale guideScale)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Expected I4, but got Unknown
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		GuideScale.ScaleAxis axis = guideScale.axis;
		return (Vector3)((int)axis switch
		{
			3 => new Vector3(0.25f, 0.25f, 0.25f) * 0.25f / DEFAULT_SCALE_POS_XYZ_DIST,
			0 => new Vector3(0.25f, 0f, 0f),
			1 => new Vector3(0f, 0.25f, 0f),
			2 => new Vector3(0f, 0f, 0.25f),
			_ => Vector3.zero,
		});
	}

	private Material CreateForceDrawMaterial()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Expected O, but got Unknown
		try
		{
			Material val = new Material(MaterialHelper.GetColorZOrderShader());
			Color red = Color.red;
			red.a = markerOverlayAlpha;
			val.SetColor("_Color", red);
			return val;
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.ToString());
			return null;
		}
	}
}
