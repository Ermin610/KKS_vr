using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Studio;
using UnityEngine;
using VRGIN.Controls;
using VRGIN.Controls.Handlers;
using VRGIN.Controls.Tools;
using VRGIN.Core;
using VRGIN.Helpers;
using VRGIN.Visuals;
using Valve.VR;

namespace KKCharaStudioVR;

/// <summary>
/// Edge logs for locomotion gates. A lock that is already true before it has
/// ever been observed clear (startup / scene-load false positive) is ignored
/// so it cannot disable movement for the rest of the session.
/// </summary>
internal static class LocomotionInputGate
{
	private const float StartupGraceSeconds = 5f;
	private static readonly Dictionary<string, bool> Armed = new Dictionary<string, bool>();
	private static readonly Dictionary<string, bool> Blocking = new Dictionary<string, bool>();
	private static readonly Dictionary<string, bool> Ignored = new Dictionary<string, bool>();
	private static readonly Dictionary<string, float> SuspectUntil = new Dictionary<string, float>();
	private static bool _bootSuspectStarted;
	private static float _bootSuspectUntil;

	internal static void DisarmStuckGates()
	{
		float until = Time.unscaledTime + StartupGraceSeconds;
		Disarm("BlocksNormalInput", until);
		Disarm("IsManualMovementLocked", until);
		Disarm("IsPlaybackInputLocked", until);
	}

	private static void Disarm(string name, float until)
	{
		Armed[name] = false;
		Blocking[name] = false;
		Ignored[name] = false;
		SuspectUntil[name] = until;
	}

	private static float SuspectDeadline(string name)
	{
		if (SuspectUntil.TryGetValue(name, out float until))
			return until;
		if (!_bootSuspectStarted)
		{
			_bootSuspectStarted = true;
			_bootSuspectUntil = Time.unscaledTime + StartupGraceSeconds;
		}
		SuspectUntil[name] = _bootSuspectUntil;
		return _bootSuspectUntil;
	}

	internal static bool Note(string name, bool active)
	{
		bool was = Blocking.TryGetValue(name, out bool blocking) && blocking;
		if (active == was)
			return active;
		Blocking[name] = active;
		VRLog.Info(active
			? "Locomotion gate blocked input: " + name
			: "Locomotion gate cleared: " + name);
		return active;
	}

	internal static bool BlockUnlessStuck(string name, bool active)
	{
		bool armed = Armed.TryGetValue(name, out bool isArmed) && isArmed;
		if (!active)
		{
			// A clear sample during the startup / scene-load window does not arm
			// the gate. The false Timeline lock turns on a moment after the
			// controllers connect; arming on that earlier clear would let the
			// stuck lock suppress locomotion for the rest of the session.
			if (!armed && Time.unscaledTime >= SuspectDeadline(name))
				Armed[name] = true;
			bool wasBlocking = Blocking.TryGetValue(name, out bool blocking) && blocking;
			bool wasIgnored = Ignored.TryGetValue(name, out bool ignored) && ignored;
			Blocking[name] = false;
			Ignored[name] = false;
			if (wasBlocking || wasIgnored)
				VRLog.Info("Locomotion gate cleared: " + name);
			return false;
		}

		if (!armed)
		{
			SuspectDeadline(name);
			if (!(Ignored.TryGetValue(name, out bool ignoredNow) && ignoredNow))
			{
				Ignored[name] = true;
				VRLog.Info("Locomotion gate ignored (stuck): " + name);
			}
			return false;
		}

		if (!(Blocking.TryGetValue(name, out bool already) && already))
		{
			Blocking[name] = true;
			VRLog.Info("Locomotion gate blocked input: " + name);
		}
		return true;
	}
}

internal static class VRMotionFilter
{
	public static float DeltaTime()
	{
		float dt = Time.deltaTime;
		if (dt < 0.0001f) return 1f / 90f;
		if (dt > 0.05f) return 0.05f;
		return dt;
	}

	public static float Alpha(float omega)
	{
		float a = 1f - Mathf.Exp(-omega * DeltaTime());
		if (a < 0f) return 0f;
		if (a > 1f) return 1f;
		return a;
	}
}

[DefaultExecutionOrder(20000)]
internal sealed class VRGrabPoseCommit : MonoBehaviour
{
	public Action Commit;

	private void LateUpdate()
	{
		if (Commit != null)
			Commit();
	}
}

internal class GripMoveKKCharaStudioTool : Tool
{
	private static bool IsMmdRigLocked => VRMmdPlaybackController.MovementPlan.LockRig;
	private static bool IsMmdWorldGrabBlocked => !VRMmdPlaybackController.MovementPlan.AllowWorldGrab;
	private static bool IsMmdObjectGrabBlocked => !VRMmdPlaybackController.MovementPlan.AllowObjectGrab;
	private static GUIQuad internalGui;
	private static readonly HashSet<GripMoveKKCharaStudioTool> ActiveTools = new HashSet<GripMoveKKCharaStudioTool>();
	private KKCharaStudioVRSettings _settings;
	private GameObject mirror1;
	private Vector3 _grabLocalPosOffset;
	private Quaternion _grabLocalRotOffset;

	private bool screenGrabbed;
	private GameObject lastGrabbedObject;
	private GameObject grabbingObject;
	private MenuHandler menuHandlder;
	private GripMenuHandler gripMenuHandler;
	private IKTool ikTool;
	private float nearestGrabable = float.MaxValue;
	private string[] FINGER_KEYS = new string[5] { "j_thumb", "j_index", "j_middle", "j_ring", "j_little" };
	private static FieldInfo f_dicGuideObject = typeof(GuideObjectManager).GetField("dicGuideObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private GameObject marker;
	public GameObject target;
	private bool lockRotXZ = true;
	private float lastSnapTurnTime;
	// Edges are one-shot per physical press. A hold that is already down when
	// the tool enables (tool switch, scene load, reconnect) stays settled until
	// release so it cannot replay a click, grab, or long-press.
	private PressLatch _menuLatch;
	private PressLatch _gripLatch;
	private PressLatch _triggerLatch;
	private PressLatch _stickLatch;
	private bool _inputLatchesArmed;
	private bool _menuTracking;
	private bool _menuChorded;
	private bool _menuLongFired;
	private float _menuStartedUnscaled;
	private bool _gripOwnsAction;
	private bool _rotLockActive;
	private bool _rotLockFired;
	private float _rotLockStartedUnscaled;
	private bool _menuHandlerCaptured;
	private bool _menuHandlerWasEnabled;
	private bool _hapticFailed;
	private bool _isLeftHand;
	private bool _handResolved;
	private bool _locomotionActive;
	private bool _axisLogged;
	private bool _lastHandModelEnabled;
	private MoveableGUIObject _proximityTarget;
	private GameObject _proximityHighlight;
	private readonly Collider[] _proximityHits = new Collider[256];
	private readonly Vector3[] _grabSamples = new Vector3[8];
	private static int _sharedGuideFrame = -1;
	private static bool _guideFailLogged;
	private float _proximityRetainUntil;
	private static readonly List<MoveableGUIObject> _sharedGuideMarkers = new List<MoveableGUIObject>(256);
	// FindGripMarker walks the guide hierarchy with several Transform.Find calls.
	// Doing that for every guide on every frame is wasted work; the marker only
	// changes when the guide is rebuilt, which turns the cached entry into a Unity null.
	private static readonly Dictionary<int, MoveableGUIObject> _guideMarkerCache = new Dictionary<int, MoveableGUIObject>(256);
	private LineRenderer _grabLine;
	private Vector3 _smoothGrabPos;
	private Quaternion _smoothGrabRot;
	private bool _smoothGrabInitialized;
	private bool _grabPosePending;
	private Vector3 _pendingGrabPos;
	private Quaternion _pendingGrabRot;
	private VRGrabPoseCommit _grabCommit;
	private Vector3 _worldMoveResidual;
	private Quaternion _worldMoveResidualRot = Quaternion.identity;

	private struct PressLatch
	{
		public bool Held;
		public bool Rose;
		public bool Fell;
		private bool _settle;
		private bool _latched;

		public void Arm()
		{
			_settle = true;
			_latched = false;
			Held = false;
			Rose = false;
			Fell = false;
		}

		public void Observe(bool held, bool apiDown)
		{
			Rose = false;
			Fell = false;
			if (_settle)
			{
				Held = held;
				_latched = held;
				if (!held)
					_settle = false;
				return;
			}

			if (Held && !held)
				Fell = true;
			// stateDown can stick for more than one frame on OpenXR, and stateDown
			// can also be dropped. A rising level is the edge; the latch ignores
			// repeats until the button is actually up.
			if (!_latched && held && (apiDown || !Held))
			{
				Rose = true;
				_latched = true;
			}
			if (!held)
				_latched = false;
			Held = held;
		}
	}

	public override Texture2D Image => UnityHelper.LoadImage("icon_gripmove.png");

	public GUIQuad Gui { get; private set; }

	internal static bool AnyObjectInteractionActive
	{
		get
		{
			foreach (GripMoveKKCharaStudioTool tool in ActiveTools)
			{
				if (tool != null && tool.HasObjectInteraction)
					return true;
			}
			return false;
		}
	}

	private bool HasObjectInteraction
	{
		get
		{
			if (grabbingObject != null) return true;
			SteamVR_Controller.Device device = controller;
			return screenGrabbed && lastGrabbedObject != null && device != null
				&& device.GetPress(EVRButtonId.k_EButton_Grip);
		}
	}

	private SteamVR_Controller.Device controller
	{
		get
		{
#if KKS
			// The tool is added onto the VRGIN controller object. Buttons and
			// sticks come from that pose's actions; pose validity is not required.
			VRGIN.Controls.Controller ctrl = Owner
				?? ((Component)this).GetComponent<VRGIN.Controls.Controller>()
				?? ((Component)this).GetComponentInParent<VRGIN.Controls.Controller>();
			if (ctrl == null && _handResolved && VR.Mode != null)
				ctrl = _isLeftHand ? VR.Mode.Left : VR.Mode.Right;
			if (ctrl != null)
			{
				SteamVR_Controller.Device dev = SteamVR_Controller.ForController(ctrl);
				if (dev != null)
					return dev;
			}
#endif
			SteamVR_TrackedObject component = ((Component)this).GetComponent<SteamVR_TrackedObject>()
				?? ((Component)this).GetComponentInParent<SteamVR_TrackedObject>();
			if (component != null && component.index != SteamVR_TrackedObject.EIndex.None)
				return SteamVR_Controller.Input((int)component.index);
			return null;
		}
	}

	private static bool _originLogged;

	internal static Transform PlayerOrigin()
	{
		if (VR.Camera == null || VR.Camera.SteamCam == null)
			return null;
		Transform origin = VR.Camera.SteamCam.origin;
		Transform head = VR.Camera.SteamCam.head;
#if KKS
		Transform tracked = VR.Camera.TrackedPose != null
			? ((Component)VR.Camera.TrackedPose).transform
			: null;
		bool headFollowsOrigin = origin != null && head != null && origin != head && head.IsChildOf(origin);
		if (headFollowsOrigin)
		{
			LogOrigin(origin, head, "SteamCam.origin");
			return origin;
		}
		Transform pose = tracked != null ? tracked : (head != null ? head : ((Component)VR.Camera).transform);
		if (pose != null && pose.parent != null)
		{
			LogOrigin(pose.parent, pose, "parent of tracked camera");
			return pose.parent;
		}
#endif
		LogOrigin(origin, head, "SteamCam.origin");
		return origin;
	}

	private static void LogOrigin(Transform origin, Transform head, string via)
	{
		if (_originLogged || origin == null)
			return;
		_originLogged = true;
		string headName = head != null ? ((UnityEngine.Object)head).name : "null";
		bool under = head != null && head.IsChildOf(origin);
		VRLog.Info("Locomotion origin: '" + ((UnityEngine.Object)origin).name + "' via " + via
			+ " head='" + headName + "' headUnderOrigin=" + under);
	}

	private void EnsureHandResolved()
	{
		if (_handResolved)
			return;
		VRGIN.Controls.Controller ctrl = Owner
			?? ((Component)this).GetComponent<VRGIN.Controls.Controller>()
			?? ((Component)this).GetComponentInParent<VRGIN.Controls.Controller>();
		VRGIN.Controls.LeftController left = ((Component)this).GetComponent<VRGIN.Controls.LeftController>();
		VRGIN.Controls.RightController right = ((Component)this).GetComponent<VRGIN.Controls.RightController>();
		if (left != null || right != null)
		{
			_isLeftHand = left != null;
			_handResolved = true;
			VRLog.Info("GripMove hand " + (_isLeftHand ? "left" : "right"));
			return;
		}
		if (ctrl != null)
		{
			if (ctrl is VRGIN.Controls.LeftController)
			{
				_isLeftHand = true;
				_handResolved = true;
				VRLog.Info("GripMove hand left via Controller type");
				return;
			}
			if (ctrl is VRGIN.Controls.RightController)
			{
				_isLeftHand = false;
				_handResolved = true;
				VRLog.Info("GripMove hand right via Controller type");
				return;
			}
			if (VR.Mode != null)
			{
				if (ctrl == VR.Mode.Left)
				{
					_isLeftHand = true;
					_handResolved = true;
					VRLog.Info("GripMove hand left via VR.Mode.Left");
					return;
				}
				if (ctrl == VR.Mode.Right)
				{
					_isLeftHand = false;
					_handResolved = true;
					VRLog.Info("GripMove hand right via VR.Mode.Right");
					return;
				}
			}
			if (ctrl.Tracking != null)
			{
				_isLeftHand = ctrl.Tracking.inputSource == SteamVR_Input_Sources.LeftHand;
				_handResolved = true;
				VRLog.Info("GripMove hand " + (_isLeftHand ? "left" : "right"));
			}
		}
	}

	private static readonly string GateControllerL = "controller.L";
	private static readonly string GateControllerR = "controller.R";
	private static readonly string GateTrackingL = "hasTracking.L";
	private static readonly string GateTrackingR = "hasTracking.R";
	private static readonly string GateLaserL = "LaserVisible.L";
	private static readonly string GateLaserR = "LaserVisible.R";

	private void resetGUIPosition()
	{
		Transform head = VR.Camera.Head;
		((Component)internalGui).transform.parent = ((Component)this).transform;
		float guiScale = _settings != null ? _settings.UISpawnScale : KKCharaStudioVRSettings.DefaultUISpawnScale;
		((Component)internalGui).transform.localScale = VRCameraMoveHelper.GetMainUIScale(guiScale);
		if (head != null)
		{
			float dist = Mathf.Clamp(
				_settings != null ? _settings.UISpawnDistance : KKCharaStudioVRSettings.DefaultUISpawnDistance,
				VRCameraMoveHelper.MinMainUIDistance,
				VRCameraMoveHelper.MaxMainUIDistance);
			((Component)internalGui).transform.position = head.TransformPoint(new Vector3(0f, 0f, dist));
			((Component)internalGui).transform.rotation = Quaternion.LookRotation(head.TransformVector(new Vector3(0f, 0f, 1f)), head.up);
		}
		else
		{
			((Component)internalGui).transform.localPosition = new Vector3(0f, 0.05f, -0.06f);
			((Component)internalGui).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		}
		((Component)internalGui).transform.parent = ((Component)this).transform.parent;
		internalGui.UpdateAspect();
		if (VRQuickActions.Instance != null)
			VRQuickActions.Instance.RememberMainGUIScale(internalGui);
	}

	internal static void CancelAllTimelineLockedInteraction()
	{
		// Work from a snapshot because releasing a tool can indirectly change the
		// active tool set during a controller lifecycle transition.
		var tools = new List<GripMoveKKCharaStudioTool>(ActiveTools);
		foreach (GripMoveKKCharaStudioTool tool in tools)
		{
			if (tool != null)
				tool.SuppressTimelineLockedInteraction();
		}
	}

	private IEnumerator DelayedResetGUI()
	{
		// Wait for VR camera head to be fully initialized
		yield return new WaitForSeconds(1.0f);
		if (internalGui != null)
		{
			resetGUIPosition();
		}
	}



	protected override void OnDestroy()
	{
		ActiveTools.Remove(this);
		ReleaseToolInteraction();
		// Runtime Material instances are not released with their GameObject.
		DestroyOwnedMaterial(_proximityHighlight != null ? _proximityHighlight.GetComponent<Renderer>() : null);
		DestroyOwnedMaterial(_grabLine);
		if (_proximityHighlight != null)
			UnityEngine.Object.Destroy(_proximityHighlight);
		if (_grabLine != null)
			UnityEngine.Object.Destroy(((Component)_grabLine).gameObject);
		if (marker != null)
			UnityEngine.Object.Destroy(marker);
		if (mirror1 != null)
			UnityEngine.Object.Destroy(mirror1);
		if (_grabCommit != null)
		{
			_grabCommit.Commit = null;
			UnityEngine.Object.Destroy(_grabCommit);
			_grabCommit = null;
		}
		// Note: We no longer destroy internalGui here because it is a static shared instance across both controllers
	}

	private static void DestroyOwnedMaterial(Renderer renderer)
	{
		if (renderer == null)
			return;
		Material material = renderer.sharedMaterial;
		if (material != null)
			UnityEngine.Object.Destroy(material);
	}

	protected override void OnStart()
	{
		base.OnStart();
		try
		{
			VRLog.Info("Loading GripMoveTool");
			_settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
			if (internalGui == null)
			{
				internalGui = GUIQuad.Create();
				((Component)internalGui).gameObject.AddComponent<MoveableGUIObject>();
				internalGui.IsOwned = true;
				UnityEngine.Object.DontDestroyOnLoad(((Component)internalGui).gameObject);
				// Delay position reset so VR camera head is fully initialized
				((MonoBehaviour)this).StartCoroutine(DelayedResetGUI());
			}

			gripMenuHandler = ((Component)this).gameObject.AddComponent<GripMenuHandler>();
			((Behaviour)gripMenuHandler).enabled = true;
		}
		catch (Exception obj)
		{
			VRLog.Info(obj);
		}
		if (marker == null)
		{
			marker = new GameObject("__GripMoveMarker__");
			marker.transform.parent = ((Component)this).transform.parent;
			marker.transform.position = ((Component)this).transform.position;
			marker.transform.rotation = ((Component)this).transform.rotation;
		}
		if (_grabCommit == null)
		{
			_grabCommit = ((Component)this).gameObject.AddComponent<VRGrabPoseCommit>();
			_grabCommit.Commit = CommitGrabPose;
		}
		menuHandlder = ((Component)this).GetComponent<MenuHandler>();
		// GripMenuHandler owns the studio pointer while this tool is active.
		// The stock handler is restored when the tool is disabled so the next
		// tool does not inherit a dead menu laser.
		SuppressStockMenuHandler();
		ikTool = IKTool.instance;
		EnsureHandResolved();
		ActiveTools.Add(this);
		_lastHandModelEnabled = _settings != null && _settings.HandModelEnabled;
		ApplyHandModelState(_lastHandModelEnabled);
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		ActiveTools.Remove(this);
		ReleaseToolInteraction();
		if (gripMenuHandler != null)
			((Behaviour)gripMenuHandler).enabled = false;
		RestoreStockMenuHandler();

		// Don't hide hand models during tool switching — keep them visible
		// Only hide if settings explicitly disable hand models
		bool handEnabled = _settings != null && _settings.HandModelEnabled;
		if (!handEnabled)
		{
			if (VRHandModelManager.Instance != null)
				VRHandModelManager.Instance.SetHandVisible(_isLeftHand, false);
			if (Owner != null)
				Owner.SetRenderModelVisible(true);
		}
	}

	private void ApplyHandModelState(bool handEnabled)
	{
		if (handEnabled)
		{
			if (VRHandModelManager.Instance != null)
				VRHandModelManager.Instance.SetHandVisible(_isLeftHand, true);
			if (Owner != null)
			{
				Owner.SetRenderModelVisible(false);
				// Hide the VRGIN tool icon circle and alpha concealer sphere
				var canvas = ((Component)Owner).GetComponentInChildren<Canvas>(true);
				if (canvas != null) ((Component)canvas).gameObject.SetActive(false);
				// Hide AlphaConcealer — it's a Sphere primitive parented directly to Controller
				HideAlphaConcealer(true);
			}
		}
		else
		{
			if (VRHandModelManager.Instance != null)
				VRHandModelManager.Instance.SetHandVisible(_isLeftHand, false);
			if (Owner != null)
			{
				Owner.SetRenderModelVisible(true);
				var canvas = ((Component)Owner).GetComponentInChildren<Canvas>(true);
				if (canvas != null) ((Component)canvas).gameObject.SetActive(true);
				HideAlphaConcealer(false);
			}
		}
	}

	private void HideAlphaConcealer(bool hide)
	{
		if (Owner == null) return;
		// The AlphaConcealer is a Sphere primitive child of the Controller transform
		// It has a MeshFilter with "Sphere" mesh and a Renderer but no name set by VRGIN
		Transform controllerT = ((Component)Owner).transform;
		for (int i = 0; i < controllerT.childCount; i++)
		{
			Transform child = controllerT.GetChild(i);
			MeshFilter mf = ((Component)child).GetComponent<MeshFilter>();
			if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.Contains("Sphere"))
			{
				((Component)child).gameObject.SetActive(!hide);
				break;
			}
		}
	}

	private void UpdateProximityDetection()
	{
		// Only when hand model is active and not currently grabbing
		if (_settings == null || !_settings.HandModelEnabled || !_settings.ProximityGrabEnabled || grabbingObject != null)
		{
			ClearProximityHighlight();
			return;
		}

		int sampleCount = CollectGrabSamples();
		float grabRadius = _settings != null ? _settings.ProximityGrabRadius : 0.12f;
		int queryMask = Physics.DefaultRaycastLayers;

		MoveableGUIObject nearest = null;
		float nearestScore = float.MaxValue;
		float nearestRaw = float.MaxValue;

		for (int s = 0; s < sampleCount; s++)
		{
			int hitCount = Physics.OverlapSphereNonAlloc(
				_grabSamples[s], grabRadius, _proximityHits, queryMask, QueryTriggerInteraction.Collide);
			for (int i = 0; i < hitCount; i++)
			{
				Collider col = _proximityHits[i];
				if (col == null) continue;
				if (col.GetComponent<VRHandContactFilter>() != null) continue;
				if (col.transform.IsChildOf(((Component)this).transform)) continue;
				ConsiderGrabTarget(ResolveGrabMarker(col), sampleCount, grabRadius, ref nearest, ref nearestScore, ref nearestRaw);
			}
		}

		ScanGuideBones(sampleCount, grabRadius, ref nearest, ref nearestScore, ref nearestRaw);
		CommitProximityTarget(nearest, nearestRaw, sampleCount, grabRadius);

		// 高亮脉冲动画（缓慢呼吸效果）
		if (_proximityHighlight != null && _proximityHighlight.activeSelf)
		{
			float pulse = 0.035f * (1f + 0.2f * Mathf.Sin(Time.time * 5f));
			_proximityHighlight.transform.localScale = Vector3.one * pulse;
		}
	}

	private int CollectGrabSamples()
	{
		int count = 0;
		_grabSamples[count++] = ((Component)this).transform.position;
		int handSamples = 0;
		if (VRHandModelManager.Instance != null)
			handSamples = VRHandModelManager.Instance.CopyGrabSamplePoints(_isLeftHand, _grabSamples, count);
		count += handSamples;
		if (handSamples == 0 && count < _grabSamples.Length)
		{
			_grabSamples[count++] = ((Component)this).transform.position
				+ ((Component)this).transform.forward * 0.05f;
		}
		return count;
	}

	private static MoveableGUIObject ResolveGrabMarker(Collider col)
	{
		if (col == null) return null;
		MoveableGUIObject mgo = col.GetComponent<MoveableGUIObject>();
		if (mgo != null) return mgo;
		mgo = col.GetComponentInParent<MoveableGUIObject>();
		if (mgo != null) return mgo;
		Transform nested = col.transform.Find("_gripmovemarker");
		if (nested != null)
			return nested.GetComponent<MoveableGUIObject>();
		return null;
	}

	private static MoveableGUIObject FindGripMarker(GuideObject guide)
	{
		if (guide == null) return null;
		Transform root = ((Component)guide).transform;
		if (root == null) return null;
		Transform marker = root.Find("_gripmovemarker");
		if (marker == null)
		{
			Transform sphere = root.Find("Sphere");
			if (sphere != null)
				marker = sphere.Find("_gripmovemarker");
		}
		if (marker == null)
		{
			int children = root.childCount;
			for (int i = 0; i < children; i++)
			{
				marker = root.GetChild(i).Find("_gripmovemarker");
				if (marker != null) break;
			}
		}
		if (marker == null) return null;
		MoveableGUIObject mgo = marker.GetComponent<MoveableGUIObject>();
		if (mgo == null || mgo.guideObject == null || mgo.guideScale != null) return null;
		return mgo;
	}

	private void ScanGuideBones(int sampleCount, float grabRadius, ref MoveableGUIObject nearest, ref float nearestScore, ref float nearestRaw)
	{
		if (f_dicGuideObject == null) return;
		if (_sharedGuideFrame != Time.frameCount)
		{
			_sharedGuideFrame = Time.frameCount;
			_sharedGuideMarkers.Clear();
			// Characters come and go; drop entries of deleted guides instead of
			// letting the cache grow for the whole session.
			if (_guideMarkerCache.Count > 1024)
				_guideMarkerCache.Clear();
			GuideObjectManager manager = Singleton<GuideObjectManager>.Instance;
			if (manager == null) return;
			try
			{
				object raw = f_dicGuideObject.GetValue(manager);
				// The non-generic enumerator boxes every DictionaryEntry, which is
				// hundreds of allocations per frame in a populated scene.
				if (raw is Dictionary<Transform, GuideObject> typed)
				{
					foreach (KeyValuePair<Transform, GuideObject> pair in typed)
						CollectGuideMarker(pair.Value);
				}
				else if (raw is System.Collections.IDictionary dic)
				{
					foreach (System.Collections.DictionaryEntry entry in dic)
						CollectGuideMarker(entry.Value as GuideObject);
				}
			}
			catch (Exception ex)
			{
				if (!_guideFailLogged)
				{
					_guideFailLogged = true;
					VRLog.Warn("Proximity guide scan failed: " + ex.Message);
				}
			}
		}
		for (int i = 0; i < _sharedGuideMarkers.Count; i++)
			ConsiderGrabTarget(_sharedGuideMarkers[i], sampleCount, grabRadius, ref nearest, ref nearestScore, ref nearestRaw);
	}

	private static void CollectGuideMarker(GuideObject guide)
	{
		try
		{
			if (guide == null || guide.transformTarget == null) return;
			if (!guide.enablePos && !guide.enableRot) return;
			if (!((Component)guide.transformTarget).gameObject.activeInHierarchy) return;
			int guideId = guide.GetInstanceID();
			MoveableGUIObject marker;
			if (!_guideMarkerCache.TryGetValue(guideId, out marker) || marker == null || marker.guideObject != guide)
			{
				marker = FindGripMarker(guide);
				if (marker != null)
					_guideMarkerCache[guideId] = marker;
				else
					_guideMarkerCache.Remove(guideId);
			}
			if (marker != null)
				_sharedGuideMarkers.Add(marker);
		}
		catch (Exception ex)
		{
			// One destroyed guide used to abort the whole scan for
			// several seconds, so a grip in that window found nothing.
			if (!_guideFailLogged)
			{
				_guideFailLogged = true;
				VRLog.Warn("Proximity guide entry skipped: " + ex.Message);
			}
		}
	}

	private void ConsiderGrabTarget(
		MoveableGUIObject mgo,
		int sampleCount,
		float grabRadius,
		ref MoveableGUIObject nearest,
		ref float nearestScore,
		ref float nearestRaw)
	{
		if (mgo == null || mgo.guideObject == null) return;
		float raw = DistanceToSamples(GrabPoint(mgo), sampleCount);
		if (mgo.guideObject.transformTarget != null && mgo.guideScale == null)
		{
			float markerDist = DistanceToSamples(mgo.transform.position, sampleCount);
			if (markerDist < raw) raw = markerDist;
		}
		if (raw > grabRadius) return;
		// Scale handles sit next to the joint. Prefer the bone unless the
		// handle is clearly the thing under the hand.
		float score = mgo.guideScale != null ? raw + 0.03f : raw;
		if (score < nearestScore)
		{
			nearestScore = score;
			nearestRaw = raw;
			nearest = mgo;
		}
	}

	private static Vector3 GrabPoint(MoveableGUIObject mgo)
	{
		if (mgo != null && mgo.guideScale == null && mgo.guideObject != null && mgo.guideObject.transformTarget != null)
			return mgo.guideObject.transformTarget.position;
		return ((Component)mgo).transform.position;
	}

	private float DistanceToSamples(Vector3 point, int sampleCount)
	{
		float best = float.MaxValue;
		for (int i = 0; i < sampleCount; i++)
		{
			float dist = Vector3.Distance(_grabSamples[i], point);
			if (dist < best) best = dist;
		}
		return best;
	}

	private void CommitProximityTarget(MoveableGUIObject nearest, float nearestRaw, int sampleCount, float grabRadius)
	{
		const float switchBias = 0.012f;
		if (nearest != null)
			_proximityRetainUntil = Time.unscaledTime + 0.2f;
		if (_proximityTarget != null)
		{
			float keep = DistanceToSamples(GrabPoint(_proximityTarget), sampleCount);
			bool stillNear = keep <= grabRadius * 1.15f;
			// A single missed overlap (buffer full, marker not installed yet)
			// used to clear the highlight on the exact frame the grip went down.
			bool retain = nearest == null
				&& keep <= grabRadius * 1.4f
				&& Time.unscaledTime <= _proximityRetainUntil;
			bool challenger = nearest != null && nearest != _proximityTarget && nearestRaw + switchBias < keep;
			if ((stillNear || retain) && !challenger)
			{
				if (_proximityHighlight != null && _proximityHighlight.activeSelf)
					_proximityHighlight.transform.position = GrabPoint(_proximityTarget);
				return;
			}
		}

		if (nearest != _proximityTarget)
		{
			_proximityTarget = nearest;
			if (nearest == null)
			{
				if (_proximityHighlight != null)
					_proximityHighlight.SetActive(false);
			}
			else
			{
				ShowProximityHighlight(nearest);
				if (_proximityHighlight != null)
					_proximityHighlight.transform.position = GrabPoint(nearest);
			}
		}
		else if (_proximityTarget != null && _proximityHighlight != null && _proximityHighlight.activeSelf)
		{
			_proximityHighlight.transform.position = GrabPoint(_proximityTarget);
		}
	}

	private void ShowProximityHighlight(MoveableGUIObject target)
	{
		if (_proximityHighlight == null)
		{
			_proximityHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			((UnityEngine.Object)_proximityHighlight).name = "_VRProximityHighlight";
			UnityEngine.Object.Destroy(_proximityHighlight.GetComponent<Collider>());
			Renderer r = _proximityHighlight.GetComponent<Renderer>();
			r.material = new Material(MaterialHelper.GetColorZOrderShader());
			r.material.color = new Color(0f, 1f, 0.5f, 0.35f);
			r.material.renderQueue = 3500;
		}
		_proximityHighlight.SetActive(true);
		_proximityHighlight.transform.position = ((Component)target).transform.position;
		_proximityHighlight.transform.localScale = Vector3.one * 0.035f;
	}

	private void ClearProximityHighlight()
	{
		_proximityTarget = null;
		if (_proximityHighlight != null)
			_proximityHighlight.SetActive(false);
	}

	private void UpdateGrabLine()
	{
		if (grabbingObject == null)
		{
			if (_grabLine != null)
				((Component)_grabLine).gameObject.SetActive(false);
			return;
		}

		// 只对有 guideObject 的 IK 目标显示连线
		MoveableGUIObject mgo = grabbingObject.GetComponent<MoveableGUIObject>();
		if (mgo == null || mgo.guideObject == null)
		{
			if (_grabLine != null)
				((Component)_grabLine).gameObject.SetActive(false);
			return;
		}

		// 延迟创建 LineRenderer
		if (_grabLine == null)
		{
			GameObject lineObj = new GameObject("_VRGrabLine");
			UnityEngine.Object.DontDestroyOnLoad(lineObj);
			_grabLine = lineObj.AddComponent<LineRenderer>();
			_grabLine.material = new Material(MaterialHelper.GetColorZOrderShader());
			_grabLine.material.renderQueue = 3600;
			_grabLine.SetVertexCount(2);
			_grabLine.useWorldSpace = true;
			_grabLine.SetWidth(0.005f, 0.002f);
		}

		((Component)_grabLine).gameObject.SetActive(true);

		Transform bone = mgo.guideObject.transformTarget;
		if (bone == null)
		{
			((Component)_grabLine).gameObject.SetActive(false);
			return;
		}
		Vector3 handPos = ((Component)this).transform.position;
		Vector3 targetPos = bone.position;
		float dist = Vector3.Distance(handPos, targetPos);

		// 颜色根据距离变化：近=绿色，中=黄色，远=橙色
		Color lineColor;
		if (dist < 0.1f)
			lineColor = new Color(0f, 1f, 0.5f, 0.4f);
		else if (dist < 0.3f)
			lineColor = new Color(1f, 1f, 0f, 0.5f);
		else
			lineColor = new Color(1f, 0.5f, 0f, 0.7f);

		_grabLine.material.color = lineColor;
		_grabLine.SetPosition(0, handPos);
		_grabLine.SetPosition(1, targetPos);
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		ActiveTools.Add(this);
		// Drop any grip/menu hold that belonged to the previous tool session.
		// The marker is snapped so a still-held grip cannot apply the disabled
		// interval as a world-move delta.
		ArmInputLatches();
		_gripOwnsAction = false;
		ClearWorldMoveResidual();
		SnapMarker();
		SuppressStockMenuHandler();
		if (gripMenuHandler != null)
			((Behaviour)gripMenuHandler).enabled = true;
		if ((internalGui != null))
			((Component)internalGui).gameObject.SetActive(true);

		bool handEnabled = _settings != null && _settings.HandModelEnabled;
		_lastHandModelEnabled = handEnabled;
		ApplyHandModelState(handEnabled);
	}

	#if KKS
	private void OnLevelWasLoaded(int level)
#else
	protected override void OnLevel(int level)
#endif
	{
		#if !KKS
		base.OnLevel(level);
#endif
		((MonoBehaviour)this).StopAllCoroutines();
		ReleaseToolInteraction();
		LocomotionInputGate.DisarmStuckGates();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		EnsureHandResolved();
		// Sticks and buttons stay live without a valid pose. Grabbing below
		// still waits for hasTracking because it needs a hand position.
		SteamVR_Controller.Device device = controller;
		if (LocomotionInputGate.Note(_isLeftHand ? GateControllerL : GateControllerR, device == null || !device.connected))
		{
			ArmInputLatches();
			SuppressTimelineLockedInteraction();
			return;
		}
		ObserveButtons(device);
		if (!_originLogged)
			LocomotionInputGate.Note("locomotion-origin", PlayerOrigin() == null);

		// 运行时检测设置变化
		bool currentHandEnabled = _settings != null && _settings.HandModelEnabled;
		if (currentHandEnabled != _lastHandModelEnabled)
		{
			_lastHandModelEnabled = currentHandEnabled;
			ApplyHandModelState(currentHandEnabled);
		}

		if (IsMmdRigLocked)
		{
			// Even when MMD playback suppresses normal movement and grabbing,
			// an unchorded stick click on either hand must toggle play/pause.
			// It must not reset the camera trim or the rig orientation.
			bool clickChorded = _gripLatch.Held || _triggerLatch.Held || _menuLatch.Held;
			bool stickClick = _stickLatch.Rose && !clickChorded;
			if (stickClick)
			{
				VRMmdPlaybackController.TryHandleLeftStickPlaybackToggle();
				VRLog.Info((_isLeftHand ? "Left" : "Right") + " stick click toggled MMD playback during active playback");
			}
			else if (!_isLeftHand && device != null)
			{
				Vector2 stick = device.GetAxis(EVRButtonId.k_EButton_Axis0);
				if (stick.sqrMagnitude < 0.01f)
					stick = device.GetAxis(EVRButtonId.k_EButton_Axis2);

				if (stick.sqrMagnitude > 0.01f)
				{
					bool gripHeld = _gripLatch.Held;
					VRMmdPlaybackController.ApplyRightStickCameraTrim(stick, gripHeld, Time.unscaledDeltaTime);
				}
			}
			SuppressTimelineLockedInteraction();
			return;
		}

		bool wristMenuOpen = !_isLeftHand && VRWristMenuController.IsOpen;
		if (!_isLeftHand)
			LocomotionInputGate.Note("VRWristMenuController.IsOpen", VRWristMenuController.IsOpen);
		if (wristMenuOpen)
		{
			// Keep right-stick turning available while the wrist menu owns the
			// trigger. Vertical locomotion stays disabled so menu scrolling cannot
			// accidentally change the player's height.
			if (HandleThumbstickLocomotion(true))
			{
				SuppressTimelineLockedInteraction();
				return;
			}
			SetComfortMoving(false);
			ClearProximityHighlight();
			if (grabbingObject != null || screenGrabbed)
				ReleaseActiveGrab(false);
			if (_grabLine != null)
				((Component)_grabLine).gameObject.SetActive(false);
			ClearWorldMoveResidual();
			// The hand keeps moving while the menu is open. Leave the marker on
			// the hand so closing the menu cannot apply that whole delta.
			SnapMarker();
			return;
		}

		if (HandleThumbstickLocomotion(false))
		{
			SuppressTimelineLockedInteraction();
			return;
		}

		if (LocomotionInputGate.BlockUnlessStuck("IsManualMovementLocked", VRTimelineCameraFollowController.IsManualMovementLocked))
		{
			SuppressTimelineLockedInteraction();
			return;
		}

		bool poseReady = device.hasTracking;
		bool pointerOnUi = gripMenuHandler != null && gripMenuHandler.IsHittingUI;
		if (pointerOnUi)
		{
			// The ray is on the panel. Do not highlight or arm a bone that sits
			// behind it. An already-held grab keeps updating until the grip lifts.
			ClearProximityHighlight();
		}
		else if (poseReady && !IsMmdObjectGrabBlocked)
		{
			// Hidden IK guides stay grabbable. Only their renderers are off.
			UpdateProximityDetection();
		}
		else
		{
			ClearProximityHighlight();
		}

		// Hiding IK controls must not disable the main VR UI or world locomotion.
		// B/Y long-press does not need a tracked pose.
		HandleButtonEvents();
		if (LocomotionInputGate.Note(_isLeftHand ? GateTrackingL : GateTrackingR, !poseReady))
		{
			// Pose loss skips the grab update below. Still honor a grip release
			// so a dropped press-up cannot leave the bone held.
			EndGripIfReleased();
			SnapMarker();
			ClearWorldMoveResidual();
			return;
		}

		HandleObjectGrab();
		HandleGripWorldMove();
		UpdateGrabLine();

		if (lastGrabbedObject != null && grabbingObject == null)
		{
			float dist = Vector3.Distance(((Component)this).transform.position, lastGrabbedObject.transform.position);
			if (dist > 0.25f)
			{
				lastGrabbedObject = null;
				screenGrabbed = false;
			}
		}
		nearestGrabable = float.MaxValue;
		SnapMarker();
	}

	private void SetComfortMoving(bool moving)
	{
		if (VRComfortVignette.Instance != null)
			VRComfortVignette.Instance.SetMoving(_isLeftHand, moving);
	}

	private void ReleaseActiveGrab(bool pulseHaptic)
	{
		try
		{
			if (grabbingObject != null)
			{
				MoveableGUIObject mgo = grabbingObject.GetComponent<MoveableGUIObject>();
				if (mgo != null && mgo.guideObject != null && VRFigurePose.IsEnabled)
					VRFigurePose.Schedule(mgo.guideObject);
				if (mgo != null)
					mgo.OnReleased();
				if (pulseHaptic && controller != null)
					controller.TriggerHapticPulse(800, EVRButtonId.k_EButton_Axis0);
			}
		}
		catch (Exception ex)
		{
			VRLog.Warn("Failed to finish VR grab cleanly: " + ex.Message);
		}
		finally
		{
			_smoothGrabInitialized = false;
			_grabPosePending = false;
			grabbingObject = null;
			screenGrabbed = false;
			lastGrabbedObject = null;
		}
	}

	private bool HandleThumbstickLocomotion(bool wristMenuOpen)
	{
		// A stick click that paused or resumed MMD owns the stick until it is
		// released. Deflection during that press must not snap-turn or walk.
		if (VRMmdPlaybackController.SuppressStickLocomotion)
		{
			SetComfortMoving(false);
			return true;
		}
		if (IsMmdRigLocked)
		{
			bool chord = _gripLatch.Held || _triggerLatch.Held || _menuLatch.Held;
			if (_stickLatch.Rose && !chord)
			{
				VRMmdPlaybackController.TryHandleLeftStickPlaybackToggle();
				VRLog.Info("Thumbstick locomotion stick click toggling MMD playback");
			}
			else if (!_isLeftHand && controller != null)
			{
				Vector2 stick = controller.GetAxis(EVRButtonId.k_EButton_Axis0);
				if (stick.sqrMagnitude < 0.01f)
					stick = controller.GetAxis(EVRButtonId.k_EButton_Axis2);
				if (stick.sqrMagnitude > 0.01f)
				{
					bool gripHeld = _gripLatch.Held;
					VRMmdPlaybackController.ApplyRightStickCameraTrim(stick, gripHeld, Time.unscaledDeltaTime);
				}
			}
			SetComfortMoving(false);
			return true;
		}
		bool laserBlocks = !wristMenuOpen && gripMenuHandler != null && gripMenuHandler.IsHittingUI;
		LocomotionInputGate.Note(_isLeftHand ? GateLaserL : GateLaserR, laserBlocks);
		if (laserBlocks)
		{
			SetComfortMoving(false);
			return false;
		}
		// Axis0 is SteamVR_Touchpad. On KKS the oculus_touch binding writes the
		// joystick position into legacy_emulate/Axis0_2D, which Device.GetAxis reads.
		Vector2 axis = controller.GetAxis(EVRButtonId.k_EButton_Axis0);
		if (!_axisLogged && axis.sqrMagnitude > 0.01f)
		{
			_axisLogged = true;
			VRLog.Info("Thumbstick Axis0 " + (_isLeftHand ? "L " : "R ") + axis.x.ToString("F2") + "," + axis.y.ToString("F2"));
		}
		bool isLeft = _isLeftHand;
		// Rose is already debounced. A stick click held with grip, trigger, or
		// B/Y belongs to that chord, not to view-reset or transport.
		bool clickChorded = _gripLatch.Held || _triggerLatch.Held || _menuLatch.Held;
		bool stickClick = _stickLatch.Rose && !clickChorded;
		if (_stickLatch.Rose && clickChorded)
			VRLog.Info((isLeft ? "L" : "R") + " stick click skipped: chord");
		bool axisMovementIntent = isLeft
			? Mathf.Abs(axis.y) > 0.1f || Mathf.Abs(axis.x) > 0.1f
			: Mathf.Abs(axis.x) > 0.1f || (!wristMenuOpen && Mathf.Abs(axis.y) > 0.1f);

		// Stick click is play/pause on either hand. Swallow the edge before
		// locomotion so the same press cannot snap-turn or reset the view.
		// A click already consumed by the presentation controller is swallowed too.
		if (stickClick || VRMmdPlaybackController.ConsumedPlaybackClickThisFrame)
		{
			if (stickClick)
			{
				bool handled = VRMmdPlaybackController.TryHandleLeftStickPlaybackToggle();
				if (!handled
					&& !isLeft
					&& VRTimelineCameraFollowController.ShouldClaimRightStickTransport)
				{
					VRTimelineCameraFollowController.TryHandleRightStickPlaybackToggle();
				}
				VRLog.Info((isLeft ? "Left" : "Right") + " stick click: MMD playback toggle");
			}
			SetComfortMoving(false);
			return true;
		}

		// While Timeline owns the camera, right-stick Y is the composition/FOV
		// input even with the wrist page open. Consume it here as well so the
		// locomotion tool does not incorrectly enable the comfort vignette.
		bool playbackLocked = LocomotionInputGate.BlockUnlessStuck(
			"IsPlaybackInputLocked", VRTimelineCameraFollowController.IsPlaybackInputLocked);
		bool manualLocked = LocomotionInputGate.BlockUnlessStuck(
			"IsManualMovementLocked", VRTimelineCameraFollowController.IsManualMovementLocked);
		bool timelineCompositionIntent = !isLeft
			&& (Mathf.Abs(axis.y) > 0.1f
				|| (controller.GetPress(EVRButtonId.k_EButton_Grip)
					&& Mathf.Abs(axis.x) > 0.1f))
			&& playbackLocked;
		if ((axisMovementIntent || timelineCompositionIntent) && manualLocked)
		{
			SetComfortMoving(false);
			return true;
		}

		if (Mathf.Abs(axis.y) > 0.1f || Mathf.Abs(axis.x) > 0.1f)
		{
			Transform head = VR.Camera != null ? VR.Camera.Head : null;
			Transform origin = PlayerOrigin();

			if (origin != null && head != null)
			{
				if (!_locomotionActive)
				{
					_locomotionActive = true;
					VRLog.Info((_isLeftHand ? "Left" : "Right") + " locomotion active");
				}
				if (isLeft)
				{
					Vector3 forward = head.forward;
					forward.y = 0f;
					if (forward.sqrMagnitude > 0.0001f)
						forward.Normalize();
					else
						forward = Vector3.forward;
					Vector3 right = head.right;
					right.y = 0f;
					if (right.sqrMagnitude > 0.0001f)
						right.Normalize();
					else
						right = Vector3.right;

					float speed = _settings != null ? _settings.LocomotionSpeed : 2.0f;
					origin.position += (forward * axis.y + right * axis.x) * speed * Time.deltaTime;
				}
				else
				{
					// Right stick X = turn, Y = height adjustment
					bool smoothTurn = _settings != null && _settings.SmoothTurnEnabled;
					if (smoothTurn)
					{
						float turnSpeed = _settings != null ? _settings.SmoothTurnSpeed : 90f;
						origin.RotateAround(head.position, Vector3.up, axis.x * turnSpeed * Time.deltaTime);
					}
					else if (Mathf.Abs(axis.x) > 0.5f)
					{
						float cooldown = _settings != null ? _settings.SnapTurnCooldown : 0.3f;
						if (Time.time - lastSnapTurnTime > cooldown)
						{
							float angle = _settings != null ? _settings.SnapTurnAngle : 45f;
							origin.RotateAround(head.position, Vector3.up, Mathf.Sign(axis.x) * angle);
							lastSnapTurnTime = Time.time;
							VRLog.Info("Snap turn " + Mathf.Sign(axis.x) * angle);
						}
					}

					// Y-axis: height (up/down) adjustment
					if (!wristMenuOpen && Mathf.Abs(axis.y) > 0.1f)
					{
						float heightSpeed = _settings != null ? _settings.LocomotionSpeed : 2.0f;
						origin.position += Vector3.up * axis.y * heightSpeed * Time.deltaTime;
					}
				}

				SetComfortMoving(true);
				LocomotionInputGate.Note("locomotion-origin", false);
			}
			else
			{
				LocomotionInputGate.Note("locomotion-origin", true);
				SetComfortMoving(false);
			}
		}
		else
		{
			if (_locomotionActive)
			{
				_locomotionActive = false;
				VRLog.Info((_isLeftHand ? "Left" : "Right") + " locomotion idle");
			}
			SetComfortMoving(false);
		}

		return false;
	}

	private void SuppressTimelineLockedInteraction()
	{
		SetComfortMoving(false);
		ClearProximityHighlight();
		if (grabbingObject != null || screenGrabbed)
			ReleaseActiveGrab(false);
		if (_grabLine != null)
			((Component)_grabLine).gameObject.SetActive(false);

		// Keep the world-grab baseline current while movement is locked. If a
		// grip remains held when Timeline is paused, the next frame starts from
		// the hand's current pose instead of applying the whole playback delta.
		if (marker != null)
		{
			marker.transform.position = ((Component)this).transform.position;
			marker.transform.rotation = ((Component)this).transform.rotation;
		}
		nearestGrabable = float.MaxValue;
		ClearWorldMoveResidual();
	}

	private void HandleButtonEvents()
	{
		if (_menuLatch.Rose)
		{
			_menuTracking = true;
			_menuChorded = false;
			_menuLongFired = false;
			_menuStartedUnscaled = Time.unscaledTime;
			VRLog.Info((_isLeftHand ? "L" : "R") + " ApplicationMenu down");
		}

		if (_menuTracking)
		{
			// Grip, trigger, or stick click during the hold is a chord. The
			// long-press GUI reset must not also run, and it must not repeat
			// while B/Y stays down.
			if (_gripLatch.Held || _triggerLatch.Held || _stickLatch.Held)
				_menuChorded = true;
			if (!_menuChorded && !_menuLongFired && Time.unscaledTime - _menuStartedUnscaled > 1.5f)
			{
				_menuLongFired = true;
				VRLog.Info("resetGUIPosition");
				try
				{
					resetGUIPosition();
				}
				catch (Exception ex)
				{
					VRLog.Warn("resetGUIPosition failed: " + ex.Message);
				}
			}
			if (_menuLatch.Fell || !_menuLatch.Held)
				_menuTracking = false;
		}

		bool rotChord = _menuLatch.Held && _triggerLatch.Held && _gripLatch.Held && !_stickLatch.Held;
		if (rotChord)
		{
			// The chord is not a world-drag. An object already in the hand keeps
			// following; a grip that was only moving the studio stops.
			if (grabbingObject == null)
				_gripOwnsAction = false;
			_menuChorded = true;
			if (!_rotLockActive)
			{
				_rotLockActive = true;
				_rotLockFired = false;
				_rotLockStartedUnscaled = Time.unscaledTime;
			}
			else if (!_rotLockFired && Time.unscaledTime - _rotLockStartedUnscaled > 0.5f)
			{
				_rotLockFired = true;
				_menuChorded = true;
				lockRotXZ = !lockRotXZ;
				VRLog.Info("Rotation lock " + (lockRotXZ ? "on" : "off"));
				if (lockRotXZ)
					ResetRotation();
			}
		}
		else
		{
			_rotLockActive = false;
			_rotLockFired = false;
		}

		// When the laser is hitting the UI quad, let GripMenuHandler handle ALL trigger input.
		// Skip trigger handling here to prevent VRToggleObjectSelectOnCursor from interfering
		// with UI clicks (dropdowns, sliders, settings panels, etc).
		if (gripMenuHandler != null && gripMenuHandler.IsHittingUI)
			return;

		// Trigger selects only by itself. The rotation-lock chord and a trigger
		// squeezed during a grip grab must not also click the workspace tree.
		bool triggerChord = _gripLatch.Held || _menuLatch.Held || _stickLatch.Held;
		if (!_triggerLatch.Rose || triggerChord)
			return;

		bool flag = false;

		// 近距离高亮目标：Trigger 点击自动在工作区树中选中
		if (_proximityTarget != null && _proximityTarget.guideObject != null)
		{
			GuideObject pg = _proximityTarget.guideObject;
			if (pg.guideSelect != null
			    && pg.guideSelect.treeNodeObject != null)
			{
				pg.guideSelect.treeNodeObject.OnClickSelect();
			}
			else
			{
				Singleton<GuideObjectManager>.Instance.selectObject = pg;
			}
			flag = true;
		}

		// 直接接触目标：Trigger 点击选中（与上面互斥）
		if (!flag && lastGrabbedObject != null && lastGrabbedObject.GetComponent<MoveableGUIObject>() != null)
		{
			GuideObject guideObject = lastGrabbedObject.GetComponent<MoveableGUIObject>().guideObject;
			if (guideObject != null)
			{
				if (guideObject.guideSelect != null && guideObject.guideSelect.treeNodeObject != null)
				{
					guideObject.guideSelect.treeNodeObject.OnClickSelect();
				}
				else
				{
					Singleton<GuideObjectManager>.Instance.selectObject = guideObject;
				}
				flag = true;
			}
		}
	}

	private void HandleObjectGrab()
	{
		if (IsMmdRigLocked)
		{
			if (grabbingObject != null || screenGrabbed)
				ReleaseActiveGrab(false);
			return;
		}
		// Grip, trigger, and stick-click together are the rotation-lock chord
		// (or a UI click). Only an unchorded grip edge may start a grab.
		bool gripChord = _triggerLatch.Held || _menuLatch.Held || _stickLatch.Held;
		if (_gripLatch.Rose && !gripChord)
			_gripOwnsAction = true;

		// A dance without a camera motion keeps the grip for world-move only.
		if (IsMmdObjectGrabBlocked)
		{
			if (grabbingObject != null || screenGrabbed)
				ReleaseActiveGrab(false);
			ClearProximityHighlight();
			EndGripIfReleased();
			return;
		}

		// Pointing at the panel blocks new world/IK grabs. Touching the quad
		// itself is still a panel grab.
		if (gripMenuHandler != null && gripMenuHandler.IsHittingUI && grabbingObject == null)
		{
			bool grabbingGui = lastGrabbedObject != null
				&& lastGrabbedObject.GetComponent<GUIQuad>() != null;
			if (!grabbingGui)
			{
				if (_gripLatch.Rose)
					_gripOwnsAction = false;
				EndGripIfReleased();
				return;
			}
		}

		// Proximity grab: when grip pressed near character IK target but not currently grabbing
		if (_gripLatch.Rose && _gripOwnsAction && grabbingObject == null && _proximityTarget != null)
		{
			screenGrabbed = true;
			lastGrabbedObject = ((Component)_proximityTarget).gameObject;
			ClearProximityHighlight();
		}

		bool pressDown = _gripLatch.Rose && _gripOwnsAction;
		bool press = _gripLatch.Held && _gripOwnsAction;

		if (pressDown && screenGrabbed && lastGrabbedObject != null)
		{
			grabbingObject = lastGrabbedObject;
			VRLog.Info("Grip grab " + grabbingObject.name);
			Transform poseSource = grabbingObject.transform;
			MoveableGUIObject grabbed = lastGrabbedObject.GetComponent<MoveableGUIObject>();
			if (grabbed != null && grabbed.guideObject != null && grabbed.guideScale == null
				&& grabbed.guideObject.transformTarget != null)
			{
				// The highlight sits on the bone. Offsetting from the marker
				// snaps the limb onto a stale guide sphere on the first commit.
				poseSource = grabbed.guideObject.transformTarget;
			}
			_grabLocalPosOffset = ((Component)this).transform.InverseTransformPoint(poseSource.position);
			_grabLocalRotOffset = Quaternion.Inverse(((Component)this).transform.rotation) * poseSource.rotation;

			if (grabbed != null && grabbed.guideObject != null)
			{
				// Hand the limb to the IK/FK solver before the first pose is written.
				// Otherwise the Animator owns it and the leg springs back each frame.
				VRIkPosing.BeginDrag(grabbed.guideObject);
				Transform bone = grabbed.guideObject.transformTarget;
				if (bone != null)
				{
					ApplyFingerFKIfNeeded(grabbed.guideObject);
					_grabLocalRotOffset = Quaternion.Inverse(((Component)this).transform.rotation) * bone.rotation;
				}
				grabbed.OnMoveStart();
			}

			SafeHaptic(1500);
			_smoothGrabInitialized = false;
		}

		if (press && grabbingObject != null)
		{
			Vector3 targetPos = ((Component)this).transform.TransformPoint(_grabLocalPosOffset);
			Quaternion targetRot = ((Component)this).transform.rotation * _grabLocalRotOffset;
			// A non-finite controller pose must not be written onto the bone.
			if (IsFinite(targetPos) && IsFinite(targetRot))
			{
			// 检测是否为 IK 目标，决定是否使用平滑插值
			MoveableGUIObject mgo = grabbingObject.GetComponent<MoveableGUIObject>();
			bool isIKTarget = mgo != null && mgo.guideObject != null;

			if (isIKTarget)
			{
				// Frame-rate independent follow. dt*k overshoots below ~15fps and
				// lets tracking noise through; a sub-millimeter deadzone holds the
				// bone still when the controller is resting.
				if (!_smoothGrabInitialized)
				{
					_smoothGrabPos = targetPos;
					_smoothGrabRot = targetRot;
					_smoothGrabInitialized = true;
				}
				else
				{
					const float positionDeadzone = 0.0003f;
					const float rotationDeadzone = 0.15f;
					if ((targetPos - _smoothGrabPos).sqrMagnitude < positionDeadzone * positionDeadzone)
						targetPos = _smoothGrabPos;
					if (Quaternion.Angle(_smoothGrabRot, targetRot) < rotationDeadzone)
						targetRot = _smoothGrabRot;
					float alpha = VRMotionFilter.Alpha(24f);
					_smoothGrabPos = Vector3.Lerp(_smoothGrabPos, targetPos, alpha);
					_smoothGrabRot = Quaternion.Slerp(_smoothGrabRot, targetRot, alpha);
				}

				// Apply after other LateUpdates so the studio IK pass cannot
				// pull the bone back for one frame and jitter the grab.
				_pendingGrabPos = _smoothGrabPos;
				_pendingGrabRot = _smoothGrabRot;
				_grabPosePending = true;
				if (_grabCommit == null)
					CommitGrabPose();
			}
			else
			{
				// 非 IK 目标（GUI 面板等）直接跟随
				_grabPosePending = false;
				grabbingObject.transform.position = targetPos;
				grabbingObject.transform.rotation = targetRot;
			}

			// IK poses are committed in LateUpdate. Calling OnMoved here would
			// write the previous bone pose and fight that commit.
			if (mgo != null && !isIKTarget)
				mgo.OnMoved();
			}
			else
			{
				_grabPosePending = false;
			}
		}

		EndGripIfReleased();
	}

	private void CommitGrabPose()
	{
		if (!_grabPosePending || grabbingObject == null)
		{
			_grabPosePending = false;
			return;
		}
		_grabPosePending = false;
		if (!IsFinite(_pendingGrabPos) || !IsFinite(_pendingGrabRot))
			return;
		grabbingObject.transform.position = _pendingGrabPos;
		grabbingObject.transform.rotation = _pendingGrabRot;
		MoveableGUIObject mgo = grabbingObject.GetComponent<MoveableGUIObject>();
		if (mgo != null)
			mgo.OnMoved();
		UpdateGrabLine();
	}

	private static bool IsFinite(Vector3 value)
	{
		return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
			&& !float.IsNaN(value.y) && !float.IsInfinity(value.y)
			&& !float.IsNaN(value.z) && !float.IsInfinity(value.z);
	}

	private static bool IsFinite(Quaternion value)
	{
		if (float.IsNaN(value.x) || float.IsInfinity(value.x)
			|| float.IsNaN(value.y) || float.IsInfinity(value.y)
			|| float.IsNaN(value.z) || float.IsInfinity(value.z)
			|| float.IsNaN(value.w) || float.IsInfinity(value.w))
			return false;
		float mag = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
		return mag > 0.0001f && mag < 4f;
	}

	private void ClearWorldMoveResidual()
	{
		_worldMoveResidual = Vector3.zero;
		_worldMoveResidualRot = Quaternion.identity;
	}

	private void ArmInputLatches()
	{
		_menuLatch.Arm();
		_gripLatch.Arm();
		_triggerLatch.Arm();
		_stickLatch.Arm();
		_menuTracking = false;
		_menuChorded = false;
		_menuLongFired = false;
		_gripOwnsAction = false;
		_rotLockActive = false;
		_rotLockFired = false;
		_inputLatchesArmed = true;
	}

	private void ObserveButtons(SteamVR_Controller.Device device)
	{
		if (!_inputLatchesArmed)
			ArmInputLatches();
		_menuLatch.Observe(
			device.GetPress(EVRButtonId.k_EButton_ApplicationMenu),
			device.GetPressDown(EVRButtonId.k_EButton_ApplicationMenu));
		_gripLatch.Observe(
			device.GetPress(EVRButtonId.k_EButton_Grip),
			device.GetPressDown(EVRButtonId.k_EButton_Grip));
		_triggerLatch.Observe(
			device.GetPress(EVRButtonId.k_EButton_Axis1),
			device.GetPressDown(EVRButtonId.k_EButton_Axis1));
		_stickLatch.Observe(
			device.GetPress(EVRButtonId.k_EButton_Axis0),
			device.GetPressDown(EVRButtonId.k_EButton_Axis0));
	}

	private void ReleaseToolInteraction()
	{
		SetComfortMoving(false);
		ReleaseActiveGrab(false);
		ClearProximityHighlight();
		HideGrabLine();
		ClearWorldMoveResidual();
		SnapMarker();
		nearestGrabable = float.MaxValue;
		_locomotionActive = false;
		ArmInputLatches();
	}

	private void SnapMarker()
	{
		if (marker == null)
			return;
		marker.transform.position = ((Component)this).transform.position;
		marker.transform.rotation = ((Component)this).transform.rotation;
	}

	private void HideGrabLine()
	{
		if (_grabLine != null)
			((Component)_grabLine).gameObject.SetActive(false);
	}

	private void EndGripIfReleased()
	{
		// A grab tracks the grip level, not only GetPressUp. OpenXR can drop
		// the up edge, and a pose-loss frame can skip the edge entirely.
		// Hover (screenGrabbed without a grab) still clears only on that edge
		// so merely not holding grip does not forget the object under the hand.
		if (!_gripLatch.Held && grabbingObject != null)
			ReleaseActiveGrab(true);
		else if (_gripLatch.Fell && screenGrabbed)
			ReleaseActiveGrab(false);
		if (!_gripLatch.Held)
			_gripOwnsAction = false;
	}

	private void SafeHaptic(ushort duration)
	{
		try
		{
			SteamVR_Controller.Device device = controller;
			if (device != null)
				device.TriggerHapticPulse(duration, EVRButtonId.k_EButton_Axis0);
		}
		catch (Exception ex)
		{
			if (_hapticFailed)
				return;
			_hapticFailed = true;
			VRLog.Warn("Grip haptic failed: " + ex.Message);
		}
	}

	private void SuppressStockMenuHandler()
	{
		if (menuHandlder == null)
			menuHandlder = ((Component)this).GetComponent<MenuHandler>();
		if (menuHandlder == null)
			return;
		if (!_menuHandlerCaptured)
		{
			_menuHandlerWasEnabled = ((Behaviour)menuHandlder).enabled;
			_menuHandlerCaptured = true;
		}
		((Behaviour)menuHandlder).enabled = false;
	}

	private void RestoreStockMenuHandler()
	{
		if (!_menuHandlerCaptured)
			return;
		_menuHandlerCaptured = false;
		if (menuHandlder != null)
			((Behaviour)menuHandlder).enabled = _menuHandlerWasEnabled;
	}

	private void HandleGripWorldMove()
	{
		if (IsMmdWorldGrabBlocked)
		{
			ClearWorldMoveResidual();
			return;
		}
		// Reserve both grips for scaling regardless of component Update order.
		if (VRTwoHandScale.Instance != null && VRTwoHandScale.Instance.ShouldSuppressWorldMove)
		{
			ClearWorldMoveResidual();
			return;
		}
		if (gripMenuHandler != null && gripMenuHandler.IsHittingUI)
		{
			ClearWorldMoveResidual();
			return;
		}

		if (_gripLatch.Held && _gripOwnsAction && grabbingObject == null)
		{
			Transform origin = PlayerOrigin();
			if (origin == null || marker == null)
			{
				ClearWorldMoveResidual();
				return;
			}
			target = ((Component)origin).gameObject;
			if (target != null)
			{
				if (mirror1 == null)
				{
					mirror1 = new GameObject("__GripMoveMirror1__");
					mirror1.transform.position = ((Component)this).transform.position;
					mirror1.transform.rotation = ((Component)this).transform.rotation;
				}
				Vector3 handPos = ((Component)this).transform.position;
				Quaternion handRot = ((Component)this).transform.rotation;
				Vector3 rawPos = marker.transform.position - handPos;
				Quaternion rawRot = RemoveLockedAxisRot(marker.transform.rotation * Quaternion.Inverse(handRot));
				Vector3 appliedPos;
				Quaternion appliedRot;
				// A tracking pop is applied whole. Smaller motion is spread
				// across frames, and the unapplied remainder is kept so the
				// world still ends on the hand instead of lagging behind it.
				if (rawPos.sqrMagnitude > 0.0625f || Quaternion.Angle(Quaternion.identity, rawRot) > 75f)
				{
					ClearWorldMoveResidual();
					appliedPos = rawPos;
					appliedRot = rawRot;
				}
				else
				{
					_worldMoveResidual += rawPos;
					_worldMoveResidualRot = rawRot * _worldMoveResidualRot;
					float alpha = VRMotionFilter.Alpha(90f);
					appliedPos = _worldMoveResidual * alpha;
					_worldMoveResidual -= appliedPos;
					appliedRot = Quaternion.Slerp(Quaternion.identity, _worldMoveResidualRot, alpha);
					_worldMoveResidualRot = Quaternion.Inverse(appliedRot) * _worldMoveResidualRot;
				}
				Transform parent = target.transform.parent;
				mirror1.transform.position = handPos;
				mirror1.transform.rotation = handRot;
				bool reparented = false;
				try
				{
					target.transform.parent = mirror1.transform;
					reparented = true;
					mirror1.transform.rotation = appliedRot * mirror1.transform.rotation;
					mirror1.transform.position = mirror1.transform.position + appliedPos;
				}
				finally
				{
					if (reparented && target != null)
						target.transform.parent = parent;
				}
			}
		}
		else
		{
			ClearWorldMoveResidual();
		}
	}

	private void ApplyFingerFKIfNeeded(GuideObject guideObject)
	{
		List<GuideObject> list = new List<GuideObject>();
		if (IsFinger(guideObject.transformTarget))
		{
			list.Add(guideObject);
		}
		foreach (GuideObject item in list)
		{
			if (item == null || item.transformTarget == null) continue;
			Vector3 stored = item.changeAmount.rot;
			if (float.IsNaN(stored.x) || float.IsInfinity(stored.x)
				|| float.IsNaN(stored.y) || float.IsInfinity(stored.y)
				|| float.IsNaN(stored.z) || float.IsInfinity(stored.z))
				continue;
			item.transformTarget.localEulerAngles = stored;
		}
	}

	private bool IsFinger(Transform t)
	{
		string[] fINGER_KEYS = FINGER_KEYS;
		foreach (string value in fINGER_KEYS)
		{
			if (((UnityEngine.Object)t).name.Contains(value))
			{
				return true;
			}
		}
		return false;
	}

	public override List<HelpText> GetHelpTexts()
	{
		return new List<HelpText>(new HelpText[3]
		{
			HelpText.Create("Thumbstick to Move/Turn", FindAttachPosition("touchpad"), new Vector3(0.06f, 0.04f, 0f)),
			HelpText.Create("Grip to grab world or UI panels", FindAttachPosition("rgrip"), new Vector3(0.06f, 0.04f, 0f)),
			HelpText.Create("Trigger to click UI or select objects", FindAttachPosition("trigger"), new Vector3(-0.06f, -0.04f, 0f))
		});
	}

	private void ResetRotation()
	{
		if (target != null)
		{
			Quaternion rotation = target.transform.rotation;
			Vector3 eulerAngles = rotation.eulerAngles;
			eulerAngles.x = 0f;
			eulerAngles.z = 0f;
			target.transform.rotation = Quaternion.Euler(eulerAngles);
		}
	}

	private IEnumerator UpdateMarkerPos()
	{
		yield return (object)new WaitForEndOfFrame();
		marker.transform.position = ((Component)this).transform.position;
		marker.transform.rotation = ((Component)this).transform.rotation;
	}

	private Quaternion RemoveLockedAxisRot(Quaternion q)
	{
		if (lockRotXZ)
		{
			return RemoveXZRot(q);
		}
		return q;
	}

	public static Quaternion RemoveXZRot(Quaternion q)
	{
		Vector3 eulerAngles = q.eulerAngles;
		eulerAngles.x = 0f;
		eulerAngles.z = 0f;
		return Quaternion.Euler(eulerAngles);
	}

	private void OnTriggerStay(Collider collider)
	{
		if (((Component)collider).GetComponent<GUIQuad>() != null)
		{
			screenGrabbed = true;
			lastGrabbedObject = ((Component)collider).gameObject;
		}
		else if (((Component)collider).GetComponent<MoveableGUIObject>() != null)
		{
			screenGrabbed = true;
			if (lastGrabbedObject != null)
			{
				Vector3 val = ((Component)collider).gameObject.transform.position - ((Component)this).transform.position;
				float sqrMagnitude = val.sqrMagnitude;
				if (sqrMagnitude < nearestGrabable)
				{
					lastGrabbedObject = ((Component)collider).gameObject;
					nearestGrabable = sqrMagnitude;
				}
			}
			else
			{
				lastGrabbedObject = ((Component)collider).gameObject;
			}
		}

	}

	private void OnTriggerEnter(Collider collider)
	{
	}

	private void OnTriggerExit(Collider collider)
	{
		GameObject gameObject = ((Component)collider).gameObject;
		if (screenGrabbed && ((Component)collider).GetComponent<MoveableGUIObject>() != null && gameObject == lastGrabbedObject)
		{

			screenGrabbed = false;
			lastGrabbedObject = null;
		}
	}
}
