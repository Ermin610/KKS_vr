using System;
using System.Collections;
using Studio;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.Events;
using UnityEngine.UI;
using VRGIN.Core;
using VRUtil;

namespace KKCharaStudioVR;

public class VRCameraMoveHelper : MonoBehaviour
{
	public bool showGUI = true;

	public RectTransform menuRect;

	private static VRCameraMoveHelper _instance;

	public bool keepY = true;

	public bool moveAlong;

	public Vector3 moveAlongBasePos;

	public Quaternion moveAlongBaseRot;

	private Studio.Studio studio;

	private GameObject moveDummy;

	private int windowID = 8752;

	private const int panelWidth = 400;

	private const int panelHeight = 100;

	private Rect windowRect = new Rect(-1f, -1f, 0f, 0f);

	private string windowTitle = "";

	private GUI.WindowFunction _windowGui;
	private GUILayoutOption[] _noLayoutOptions;
	private GUILayoutOption[] _buttonLayoutOptions;

	private float DEFAULT_DISTANCE = 3f;

	private float DISTANCE_RATIO = 1f;
	private const float MainUIBaseScale = 0.8f;
	public const float MinMainUIDistance = 0.2f;
	public const float MaxMainUIDistance = 3.0f;
	public const float MinMainUIScale = 0.2f;
	public const float MaxMainUIScale = 2.0f;

	public static VRCameraMoveHelper Instance => _instance;

	public static void Install(GameObject container)
	{
		if (_instance == null)
		{
			_instance = container.AddComponent<VRCameraMoveHelper>();
		}
	}

	private void Start()
	{
		StartCoroutine(StartupAlignCo());
	}

	private IEnumerator StartupAlignCo()
	{
		VRLog.Info("StartupAlignCo: Waiting for game to initialize...");

		// Wait until the initial main scene loading is completely finished
		var sceneManager = Singleton<Manager.Scene>.Instance;
		if (sceneManager != null)
		{
			float loadingDeadline = Time.realtimeSinceStartup + 30f;
			while (VRGameCompatibility.IsLoading)
			{
				if (Time.realtimeSinceStartup >= loadingDeadline)
				{
					VRLog.Warn("StartupAlignCo: timed out waiting for Manager.Scene loading flags.");
					break;
				}
				yield return null;
			}
		}

		// Dynamically wait until VR headset tracking and camera rigs are active
		float trackingDeadline = Time.realtimeSinceStartup + 5f;
		while (Time.realtimeSinceStartup < trackingDeadline)
		{
			if (VR.Active && VR.Camera != null && VR.Camera.Head != null && VR.Camera.Head.position != Vector3.zero)
			{
				break;
			}
			yield return null;
		}

		// Brief settling pause (0.2s) for camera rig stabilization
		yield return new WaitForSeconds(0.2f);

		VRLog.Info("StartupAlignCo: Game initialized. Aligning VR camera and UI.");

		// Teleport player VR head to the initial scene camera position
		MoveToCurrent();

		// Reposition the main floating studio UI quad directly in front of the player's eyes
		float dist = KKCharaStudioVRSettings.DefaultUISpawnDistance;
		float scale = KKCharaStudioVRSettings.DefaultUISpawnScale;
		var settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
		if (settings != null)
		{
			dist = settings.UISpawnDistance;
			scale = settings.UISpawnScale;
		}
		RepositionMainUI(dist, scale);
	}

	public static void RepositionMainUI(float dist)
	{
		float scale = KKCharaStudioVRSettings.DefaultUISpawnScale;
		if (VR.Manager != null && VR.Manager.Context != null)
		{
			KKCharaStudioVRSettings settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
			if (settings != null)
				scale = settings.UISpawnScale;
		}
		RepositionMainUI(dist, scale);
	}

	public static Vector3 GetMainUIScale(float multiplier)
	{
		float y = MainUIBaseScale * Mathf.Clamp(multiplier, MinMainUIScale, MaxMainUIScale);
		float aspect = VRGUI.Height > 0 ? (float)VRGUI.Width / VRGUI.Height : 1f;
		return new Vector3(y * aspect, y, 1f);
	}

	public static void RepositionMainUI(float dist, float scaleMultiplier)
	{
		RepositionMainUI(dist, scaleMultiplier, true);
	}

	public static void RepositionMainUI(float dist, float scaleMultiplier, bool ensureVisible)
	{
		try
		{
			Transform head = VR.Camera.Head;
			if (head == null) return;
			dist = Mathf.Clamp(dist, MinMainUIDistance, MaxMainUIDistance);

			VRGIN.Visuals.GUIQuad internalGui = GetMainUI();
			if (internalGui != null)
			{
				VRLog.Info("RepositionMainUI: Repositioning internalGui directly!");
				if (ensureVisible)
					((Component)internalGui).gameObject.SetActive(true);
				((Component)internalGui).transform.position = head.TransformPoint(new Vector3(0f, 0f, dist));
				((Component)internalGui).transform.rotation = Quaternion.LookRotation(head.TransformVector(new Vector3(0f, 0f, 1f)), head.up);
				((Component)internalGui).transform.localScale = GetMainUIScale(scaleMultiplier);
				internalGui.UpdateAspect();
				if (VRQuickActions.Instance != null)
					VRQuickActions.Instance.RememberMainGUIScale(internalGui);
			}
		}
		catch (Exception ex)
		{
			VRLog.Error($"RepositionMainUI failed: {ex}");
		}
	}

	internal static VRGIN.Visuals.GUIQuad GetMainUI()
	{
		try
		{
			Type toolType = typeof(VRCameraMoveHelper).Assembly.GetType("KKCharaStudioVR.GripMoveKKCharaStudioTool");
			if (toolType == null)
				return null;
			var guiField = toolType.GetField(
				"internalGui",
				System.Reflection.BindingFlags.Static
				| System.Reflection.BindingFlags.NonPublic
				| System.Reflection.BindingFlags.Public);
			return guiField == null
				? null
				: guiField.GetValue(null) as VRGIN.Visuals.GUIQuad;
		}
		catch
		{
			return null;
		}
	}

	private void OnLevelWasLoaded(int level)
	{
		studio = Singleton<Studio.Studio>.Instance;
		if (!(studio == null))
		{
			Transform cameraMenuRootT = ((Component)studio).transform.Find("Canvas System Menu/02_Camera");
			_instance.Init(cameraMenuRootT);
		}
	}

	private void OnGUI()
	{
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Expected O, but got Unknown
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		if (!showGUI || !(menuRect != null) || !((Component)menuRect).gameObject.activeInHierarchy)
		{
			return;
		}
		GUISkin skin = GUI.skin;
		try
		{
			GUI.skin = VRIMGUIUtil.VRGUISkin;
			if (windowRect.x == -1f && windowRect.y == -1f)
			{
				windowRect = new Rect((float)(Screen.width / 2), 60f * ((Transform)menuRect).lossyScale.y, 400f, 100f);
			}
			EnsureGuiCache();
			windowRect = GUI.Window(windowID, windowRect, _windowGui, windowTitle);
		}
		finally
		{
			GUI.skin = skin;
		}
	}

	private void FuncWindowGUI(int winID)
	{
		try
		{
			GUI.enabled = true;
			EnsureGuiCache();
			GUILayout.BeginVertical(_noLayoutOptions);
			GUILayout.BeginHorizontal(_noLayoutOptions);
			GUILayoutOption[] array = _buttonLayoutOptions;
			if (GUILayout.Button("Back(1m)", array))
			{
				MoveForwardBackward(-1f);
			}
			if (GUILayout.Button("Back(2m)", array))
			{
				MoveForwardBackward(-2f);
			}
			if (GUILayout.Button("Jump", array))
			{
				MoveToSelectedObject(lockY: true);
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal(_noLayoutOptions);
			if (GUILayout.Button("Fwd(1m)", array))
			{
				MoveForwardBackward(1f);
			}
			if (GUILayout.Button("Fwd(2m)", array))
			{
				MoveForwardBackward(2f);
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUI.DragWindow();
		}
		catch (Exception value)
		{
			Console.WriteLine(value);
		}
	}

	public void SaveCamera(int slot)
	{
		if (VR.Camera.Head == null)
			return;
		if (!TryResolveCameraCtrl(out Studio.CameraControl cameraCtrl)
			|| studio.sceneInfo == null
			|| studio.sceneInfo.cameraData == null
			|| slot < 0
			|| slot >= studio.sceneInfo.cameraData.Length)
		{
			VRLog.Warn("SaveCamera skipped: Studio camera data is not ready.");
			return;
		}
		CurrentToCameraCtrl();
		studio.sceneInfo.cameraData[slot] = cameraCtrl.Export();
	}

	public void CurrentToCameraCtrl()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		if (VR.Camera == null || VR.Camera.Head == null)
		{
			VRLog.Warn("CurrentToCameraCtrl skipped: VR head is not ready.");
			return;
		}
		if (!TryResolveCameraCtrl(out Studio.CameraControl cameraCtrl))
		{
			VRLog.Warn("CurrentToCameraCtrl skipped: Studio cameraCtrl is not ready.");
			return;
		}
		GetCurrentLookDirAndRot(out var lookPoint, out var dir, out var rot);
		var val = new Studio.CameraControl.CameraData();
		Vector3 val2 = default(Vector3);
		val2 = new Vector3(0f, 0f, -1f * DEFAULT_DISTANCE * DISTANCE_RATIO);
		val.Set(lookPoint, rot, val2, cameraCtrl.fieldOfView);
		cameraCtrl.Import(val);
	}

	private void GetCurrentLookDirAndRot(out Vector3 lookPoint, out Vector3 dir, out Vector3 rot)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		lookPoint = VR.Camera.Head.TransformPoint(Vector3.forward * DEFAULT_DISTANCE * DISTANCE_RATIO);
		Vector3 val = lookPoint;
		val.y = VR.Camera.Head.position.y;
		dir = val - VR.Camera.Head.position;
		if (dir == Vector3.zero)
		{
			dir = Vector3.forward;
		}
		Quaternion val2 = Quaternion.LookRotation(dir);
		rot = val2.eulerAngles;
	}

	public void MoveToCamera(int slot)
	{
		if (!TryResolveCameraCtrl(out Studio.CameraControl cameraCtrl)
			|| studio.sceneInfo == null
			|| studio.sceneInfo.cameraData == null
			|| slot < 0
			|| slot >= studio.sceneInfo.cameraData.Length)
		{
			VRLog.Warn("MoveToCamera skipped: Studio camera slot data is not ready.");
			return;
		}
		var val = studio.sceneInfo.cameraData[slot];
		if (val == null)
		{
			VRLog.Warn("MoveToCamera skipped: camera slot " + slot + " is empty.");
			return;
		}
		cameraCtrl.Import(val);
		MoveToCurrent();
	}

	public void MoveToCurrent()
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			// OnLevelWasLoaded does not run for the scene that is already loaded
			// when this component is created, so the cached studio reference stays
			// null through the startup alignment. KKS can also report a Studio
			// instance before cameraCtrl exists.
			if (!TryResolveCameraCtrl(out Studio.CameraControl cameraCtrl))
			{
				VRLog.Warn("MoveToCurrent skipped: Studio cameraCtrl is not ready.");
				return;
			}
			Studio.CameraControl.CameraData val = cameraCtrl.Export();
			if (val == null)
			{
				VRLog.Warn("MoveToCurrent skipped: Studio camera export returned null.");
				return;
			}
			Vector3 tobeHeadPos = val.pos + Quaternion.Euler(val.rotate) * val.distance;
			Quaternion tobeHeadRot = Quaternion.Euler(val.rotate);
			MoveTo(tobeHeadPos, tobeHeadRot);
		}
		catch (Exception ex)
		{
			VRLog.Error("MoveToCurrent failed: " + ex.Message);
		}
	}

	private bool TryResolveCameraCtrl(out Studio.CameraControl cameraCtrl)
	{
		cameraCtrl = null;
		Studio.Studio resolved = studio != null ? studio : Singleton<Studio.Studio>.Instance;
		if (resolved == null)
			return false;
		studio = resolved;
		cameraCtrl = resolved.cameraCtrl;
		return cameraCtrl != null;
	}

	public void MoveTo(Vector3 tobeHeadPos, Quaternion tobeHeadRot)
	{
		if (VR.Camera == null || VR.Camera.Head == null)
		{
			VRLog.Warn("VR.Camera or VR.Camera.Head is null, cannot MoveTo");
			return;
		}

		GameObject vROrigin = GetVROrigin();
		if (vROrigin != null)
		{
			if (moveDummy == null)
			{
				moveDummy = new GameObject("MoveDummy");
				UnityEngine.Object.DontDestroyOnLoad(moveDummy);
				moveDummy.transform.parent = ((Component)this).gameObject.transform;
			}

			Transform parent = vROrigin.transform.parent;
			moveDummy.transform.position = VR.Camera.Head.position;
			moveDummy.transform.rotation = GripMoveKKCharaStudioTool.RemoveXZRot(VR.Camera.Head.rotation);
			vROrigin.transform.parent = moveDummy.transform;
			moveDummy.transform.position = tobeHeadPos;
			moveDummy.transform.rotation = tobeHeadRot;
			vROrigin.transform.parent = parent;
			vROrigin.transform.rotation = GripMoveKKCharaStudioTool.RemoveXZRot(vROrigin.transform.rotation);
		}
	}

	private GameObject GetVROrigin()
	{
		if ((VR.Camera != null) && (VR.Camera.SteamCam != null) && (VR.Camera.SteamCam.origin != null))
		{
			return ((Component)VR.Camera.SteamCam.origin).gameObject;
		}
		return null;
	}

	public void MoveToSelectedObject(bool lockY)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		ObjectCtrlInfo[] selectObjectCtrl = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectObjectCtrl;
		if (selectObjectCtrl != null && selectObjectCtrl.Length != 0)
		{
			ObjectCtrlInfo val = selectObjectCtrl[0];
			Vector3 position = val.guideObject.transformTarget.position;
			if (val is OCIChar)
			{
				position = ((ChaInfo)((OCIChar)((val is OCIChar) ? val : null)).charInfo).objHead.transform.position;
			}
			MoveToPoint(position, lockY);
		}
	}

	public void MoveToPoint(Vector3 targetPos, bool lockY)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		GetCurrentLookDirAndRot(out var lookPoint, out var dir, out var rot);
		Vector3 val = targetPos - dir.normalized * 0.5f;
		if (lockY)
		{
			val.y = VR.Camera.Head.position.y;
		}
		else
		{
			val.y += VR.Camera.Head.position.y - lookPoint.y;
		}
		MoveTo(val, Quaternion.Euler(rot));
	}

	public void MoveForwardBackward(float distance)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		GetCurrentLookDirAndRot(out var _, out var dir, out var rot);
		Vector3 tobeHeadPos = VR.Camera.Head.position + dir * distance;
		tobeHeadPos.y = VR.Camera.Head.position.y;
		MoveTo(tobeHeadPos, Quaternion.Euler(rot));
	}

	private void EnsureGuiCache()
	{
		if (_windowGui != null)
			return;
		_windowGui = FuncWindowGUI;
		_noLayoutOptions = new GUILayoutOption[0];
		_buttonLayoutOptions = new GUILayoutOption[2]
		{
			GUILayout.Width(80f),
			GUILayout.Height(35f)
		};
	}

	private void Init(Transform cameraMenuRootT)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Expected O, but got Unknown
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Expected O, but got Unknown
		VRLog.Info("Initializing VRCameraMoveHelper");
		try
		{
			menuRect = ((Component)cameraMenuRootT).GetComponent<RectTransform>();
			if (moveDummy == null)
			{
				moveDummy = new GameObject("MoveDummy");
				UnityEngine.Object.DontDestroyOnLoad(moveDummy);
				moveDummy.transform.parent = ((Component)this).gameObject.transform;
			}
			for (int i = 0; i < ((Transform)menuRect).childCount; i++)
			{
				Transform child = ((Transform)menuRect).GetChild(i);
				int idx = -1;
				if (int.TryParse(((UnityEngine.Object)child).name, out idx))
				{
					((UnityEvent)((Component)child.Find("Button Save")).gameObject.GetComponent<Button>().onClick).AddListener((UnityAction)delegate
					{
						OnSaveButtonClick(idx);
					});
					((UnityEvent)((Component)child.Find("Button Load")).gameObject.GetComponent<Button>().onClick).AddListener((UnityAction)delegate
					{
						OnLoadButtonClick(idx);
					});
				}
				else
				{
					VRLog.Info("Not Found. {0}", ((UnityEngine.Object)child).name);
				}
			}
		}
		catch (Exception obj)
		{
			VRLog.Error(obj);
		}
		VRLog.Info("VR Camera Helper installed.");
	}

	private void OnSaveButtonClick(int idx)
	{
		SaveCamera(idx);
	}

	private void OnLoadButtonClick(int idx)
	{
		MoveToCamera(idx);
	}
}
