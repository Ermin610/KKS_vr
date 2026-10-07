using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;
using VRGIN.Controls;
using VRGIN.Core;
using VRGIN.Native;
using VRGIN.Visuals;
using Valve.VR;

namespace KKCharaStudioVR;

// Aim before GripMoveKKCharaStudioTool (default 0) so a UI hit blocks world
// input on the same frame the ray arrives, not one frame later.
[DefaultExecutionOrder(-200)]
public class GripMenuHandler : ProtectedBehaviour
{
	private class ResizeHandler : ProtectedBehaviour
	{
		private GUIQuad _Gui;

		private Vector3? _StartLeft;

		private Vector3? _StartRight;

		private Vector3? _StartScale;

		private Quaternion? _StartRotation;

		private Vector3? _StartPosition;

		private Quaternion _StartRotationController;

		private Vector3? _OffsetFromCenter;

		public bool IsDragging { get; private set; }

		protected override void OnStart()
		{
			base.OnStart();
			_Gui = ((Component)this).GetComponent<GUIQuad>();
		}

		protected override void OnFixedUpdate()
		{
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_0088: Unknown result type (might be due to invalid IL or missing references)
			//IL_0095: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0101: Unknown result type (might be due to invalid IL or missing references)
			//IL_0106: Unknown result type (might be due to invalid IL or missing references)
			//IL_010b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0121: Unknown result type (might be due to invalid IL or missing references)
			//IL_0126: Unknown result type (might be due to invalid IL or missing references)
			//IL_013b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0143: Unknown result type (might be due to invalid IL or missing references)
			//IL_0148: Unknown result type (might be due to invalid IL or missing references)
			//IL_015d: Unknown result type (might be due to invalid IL or missing references)
			//IL_015f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0162: Unknown result type (might be due to invalid IL or missing references)
			//IL_0167: Unknown result type (might be due to invalid IL or missing references)
			//IL_016c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0177: Unknown result type (might be due to invalid IL or missing references)
			//IL_017c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0181: Unknown result type (might be due to invalid IL or missing references)
			base.OnFixedUpdate();
			IsDragging = false; // Disabled two-handed UI moving/scaling to prevent accidental triggering
			if (IsDragging)
			{
				if (!_StartScale.HasValue)
				{
					Initialize();
				}
				Vector3 position = ((Component)VR.Mode.Left).transform.position;
				Vector3 position2 = ((Component)VR.Mode.Right).transform.position;
				float num = Vector3.Distance(position, position2);
				float num2 = Vector3.Distance(_StartLeft.Value, _StartRight.Value);
				Vector3 val = position2 - position;
				Vector3 val2 = position + val * 0.5f;
				Quaternion val3 = Quaternion.Inverse(VR.Camera.SteamCam.origin.rotation);
				Quaternion averageRotation = GetAverageRotation();
				Quaternion val4 = val3 * averageRotation * Quaternion.Inverse(val3 * _StartRotationController);
				((Component)_Gui).transform.localScale = num / num2 * _StartScale.Value;
				((Component)_Gui).transform.localRotation = val4 * _StartRotation.Value;
				((Component)_Gui).transform.position = val2 + averageRotation * Quaternion.Inverse(_StartRotationController) * _OffsetFromCenter.Value;
			}
			else
			{
				_StartScale = null;
			}
		}

		private Quaternion GetAverageRotation()
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_0069: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			Vector3 position = ((Component)VR.Mode.Left).transform.position;
			Vector3 val = ((Component)VR.Mode.Right).transform.position - position;
			Vector3 normalized = val.normalized;
			Vector3 val2 = Vector3.Lerp(((Component)VR.Mode.Left).transform.forward, ((Component)VR.Mode.Right).transform.forward, 0.5f);
			val = Vector3.Cross(normalized, val2);
			return Quaternion.LookRotation(val.normalized, val2);
		}

		private void Initialize()
		{
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0065: Unknown result type (might be due to invalid IL or missing references)
			//IL_0080: Unknown result type (might be due to invalid IL or missing references)
			//IL_0091: Unknown result type (might be due to invalid IL or missing references)
			//IL_0096: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00de: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
			_StartLeft = ((Component)VR.Mode.Left).transform.position;
			_StartRight = ((Component)VR.Mode.Right).transform.position;
			_StartScale = ((Component)_Gui).transform.localScale;
			_StartRotation = ((Component)_Gui).transform.localRotation;
			_StartPosition = ((Component)_Gui).transform.position;
			_StartRotationController = GetAverageRotation();
			Vector3.Distance(_StartLeft.Value, _StartRight.Value);
			Vector3 val = _StartRight.Value - _StartLeft.Value;
			Vector3 val2 = _StartLeft.Value + val * 0.5f;
			_OffsetFromCenter = ((Component)this).transform.position - val2;
		}

		private SteamVR_Controller.Device GetDevice(Controller controller)
		{
			return SteamVR_Controller.Input(VRGameCompatibility.DeviceIndex(controller));
		}
	}

	private const float RESIZE_RATE = 0.01f;

	private const int MOUSE_STABILIZER_THRESHOLD = 30;

	public static GripMenuHandler ActiveMouseHandler;

	private Controller _Controller;

	private const float RANGE = 0.25f;

	private LineRenderer Laser;

	private GameObject dotCursor;

	private Vector2? mouseDownPosition;

	private GUIQuad _Target;

	private ResizeHandler _ResizeHandler;

	private Vector3 _ScaleVector;

	private float _lastScrollTime;

	private Renderer _dotRenderer;

	private Material _laserMaterial;

	private Material _dotMaterial;

	private static Texture2D _cursorTexture;

	private static bool _simulatedCursorDisabled;

	private Color _currentLaserColor = Color.cyan;

	private Color _currentDotColor = Color.cyan;

	private Vector2 _lastHitScreenPos;

	private Vector2 _lastHitUV;

	private Vector3 _lockedHitPoint;

	private bool _lockHit;

	private bool _hasValidHit;

	private bool _pointerDownActive;

	private bool _win32MouseDown;

	private bool _triggerSettle = true;

	private bool _triggerLatched;

	private bool _triggerHeld;

	private bool _pointerChorded;

	private float _lastScrollHaptic;

	private bool _hapticFailed;

	private bool _scrollFailed;

	protected SteamVR_Controller.Device Device
	{
		get
		{
			if (_Controller == null)
				_Controller = ((Component)this).GetComponent<Controller>()
					?? ((Component)this).GetComponentInParent<Controller>();
			if (_Controller == null)
				return null;
#if KKS
			// ForController reads the pose actions even when the OpenXR device
			// index is still unpublished. A valid pose is not required to click.
			return SteamVR_Controller.ForController(_Controller);
#else
			if (_Controller.Tracking == null ||
				VRGameCompatibility.DeviceIndex(_Controller) < 0)
			{
				return null;
			}
			return SteamVR_Controller.Input(VRGameCompatibility.DeviceIndex(_Controller));
#endif
		}
	}

	private bool IsResizing
	{
		get
		{
			if ((_ResizeHandler != null))
			{
				return _ResizeHandler.IsDragging;
			}
			return false;
		}
	}

	public bool LaserVisible
	{
		get
		{
			if ((Laser != null))
			{
				return ((Component)Laser).gameObject.activeSelf;
			}
			return false;
		}
		set
		{
			if (Laser == null)
			{
				if (dotCursor != null)
					dotCursor.SetActive(false);
				mouseDownPosition = null;
				return;
			}
			((Component)Laser).gameObject.SetActive(value);
			if (dotCursor != null) dotCursor.SetActive(value);
			if (value)
			{
				Laser.SetPosition(0, ((Component)Laser).transform.position);
				Laser.SetPosition(1, ((Component)Laser).transform.position);
			}
			else
			{
				mouseDownPosition = null;
			}
		}
	}

	public bool IsPressing { get; private set; }

	// True only while the ray hits the user-facing face of a GUI quad.
	public bool IsHittingUI { get; private set; }

	private string _deviceGateName;
	private const string UnknownDeviceGate = "GripMenuHandler.Device.?";

	private string HandTag
	{
		get
		{
			if (_Controller == null)
				_Controller = ((Component)this).GetComponent<Controller>()
					?? ((Component)this).GetComponentInParent<Controller>();
			if (_Controller is LeftController)
				return "L";
			if (_Controller is RightController)
				return "R";
			if (_Controller != null && _Controller.Tracking != null)
				return _Controller.Tracking.inputSource == SteamVR_Input_Sources.LeftHand ? "L" : "R";
			return "?";
		}
	}

	private string DeviceGateName()
	{
		if (_deviceGateName != null)
			return _deviceGateName;
		string tag = HandTag;
		if (tag == "?")
			return UnknownDeviceGate;
		_deviceGateName = tag == "L"
			? "GripMenuHandler.Device.L"
			: "GripMenuHandler.Device.R";
		return _deviceGateName;
	}

	protected override void OnStart()
	{
		base.OnStart();
		_Controller = ((Component)this).GetComponent<Controller>();
		_ScaleVector = (Vector2)(new Vector2(1f, 1f));
		DisableSimulatedCursor();
		if (!InitLaser())
		{
			VRLog.Error("VR UI pointer disabled because no compatible material was available.");
			enabled = false;
		}
	}

	private static void DisableSimulatedCursor()
	{
		try
		{
			if (VRGUI.Instance != null && VRGUI.Instance.SoftCursor != null)
			{
				if (VRGUI.Instance.SoftCursor.enabled || VRGUI.Instance.SoftCursor.gameObject.activeSelf)
				{
					VRGUI.Instance.SoftCursor.enabled = false;
					VRGUI.Instance.SoftCursor.gameObject.SetActive(false);
					if (!_simulatedCursorDisabled)
					{
						_simulatedCursorDisabled = true;
						VRLog.Info("Deactivated laggy 2D SimulatedCursor from VRGUI IMGUI.");
					}
				}
			}
			GameObject curObj = GameObject.Find("VRGIN_Cursor");
			if (curObj != null && curObj.activeSelf)
			{
				curObj.SetActive(false);
			}
		}
		catch
		{
		}
	}

	private static Texture2D CreateCursorTexture()
	{
		int size = 64;
		Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;
		float center = (size - 1) * 0.5f;
		float radiusRingOuter = center * 0.95f;
		float radiusRingInner = center * 0.65f;
		float radiusDot = center * 0.35f;

		Color[] colors = new Color[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
				float alpha = 0f;
				if (dist <= radiusDot)
				{
					alpha = Mathf.Clamp01((radiusDot - dist) + 0.5f);
				}
				else if (dist >= radiusRingInner && dist <= radiusRingOuter)
				{
					float dEdge = Mathf.Min(dist - radiusRingInner, radiusRingOuter - dist);
					alpha = Mathf.Clamp01(dEdge * 1.5f);
				}
				colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
			}
		}
		tex.SetPixels(colors);
		tex.Apply();
		return tex;
	}

	private bool InitLaser()
	{
		DestroyPointerVisuals();
		try
		{
			GameObject laserObject = new GameObject("KKVR_StudioUILaser");
			laserObject.SetActive(false);
			Laser = laserObject.AddComponent<LineRenderer>();
			((Component)Laser).transform.SetParent(((Component)this).transform, false);
			_laserMaterial = VRPointerVisuals.CreateMaterial("KKVR Studio UI Laser", Color.cyan);
			if (_laserMaterial == null)
				throw new InvalidOperationException("No pointer shader is available.");
			((Renderer)Laser).sharedMaterial = _laserMaterial;
			Laser.SetColors(Color.cyan, Color.cyan);
			((Component)Laser).transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
			Transform laserTransform = ((Component)Laser).transform;
			laserTransform.localPosition = laserTransform.localRotation * Vector3.forward * 0.07f;
			Laser.SetVertexCount(2);
			Laser.useWorldSpace = true;
			Laser.SetWidth(0.002f, 0.002f);

			dotCursor = GameObject.CreatePrimitive(PrimitiveType.Quad);
			dotCursor.name = "VR3DCursor";
			dotCursor.SetActive(false);
			dotCursor.transform.SetParent(null, false);
			Object.DestroyImmediate(dotCursor.GetComponent<Collider>());
			_dotRenderer = dotCursor.GetComponent<Renderer>();
			if (_cursorTexture == null)
				_cursorTexture = CreateCursorTexture();
			_dotMaterial = VRPointerVisuals.CreateMaterial("VR3D Surface Cursor", Color.cyan);
			if (_dotRenderer == null || _dotMaterial == null)
				throw new InvalidOperationException("No cursor renderer or material is available.");
			_dotMaterial.mainTexture = _cursorTexture;
			_dotRenderer.sharedMaterial = _dotMaterial;
			return true;
		}
		catch (Exception ex)
		{
			VRLog.Error("Failed to initialize VR UI pointer: " + ex.Message);
			DestroyPointerVisuals();
			return false;
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		DisableSimulatedCursor();
		if (LocomotionInputGate.Note("VRWristMenuController.IsOpen", VRWristMenuController.IsOpen))
		{
			SettleTrigger();
			ReleasePointer(false);
			IsHittingUI = false;
			if (Laser != null)
				LaserVisible = false;
			return;
		}

		bool cameraMissing = VR.Camera == null || !((Component)VR.Camera).gameObject.activeInHierarchy;
		if (LocomotionInputGate.Note("GripMenuHandler.VRCamera", cameraMissing))
		{
			SettleTrigger();
			ReleasePointer(false);
			IsHittingUI = false;
			return;
		}

		if (IsResizing)
		{
			IsHittingUI = false;
			if (Laser != null)
			{
				Laser.SetPosition(0, ((Component)Laser).transform.position);
				Laser.SetPosition(1, ((Component)Laser).transform.position);
			}
		}
		else if (Laser != null && (LaserVisible || (_Controller != null && _Controller.CanAcquireFocus())))
		{
			UpdateLaser();
		}
		else
		{
			IsHittingUI = false;
		}
		CheckInput();
	}

	protected override void OnLateUpdate()
	{
		base.OnLateUpdate();
		if (LaserVisible && _Target != null && ((Component)_Target).gameObject.activeInHierarchy)
		{
			UpdateLaser();
		}
	}

	private void OnDisable()
	{
		// A held trigger must not become a new click when this handler is enabled again.
		SettleTrigger();
		ReleasePointer(false);
		IsHittingUI = false;
		LaserVisible = false;
		if (ActiveMouseHandler == this)
		{
			ActiveMouseHandler = null;
		}
	}

	private void OnDestroy()
	{
		ReleasePointer(false);
		if (ActiveMouseHandler == this)
			ActiveMouseHandler = null;
		DestroyPointerVisuals();
	}

	private void DestroyPointerVisuals()
	{
		if (Laser != null)
		{
			Object.Destroy(((Component)Laser).gameObject);
			Laser = null;
		}
		if (dotCursor != null)
		{
			Object.Destroy(dotCursor);
			dotCursor = null;
		}
		_dotRenderer = null;
		VRPointerVisuals.DestroyMaterial(ref _laserMaterial);
		VRPointerVisuals.DestroyMaterial(ref _dotMaterial);
	}

	private void EnsureResizeHandler()
	{
		if ((_ResizeHandler == null))
		{
			_ResizeHandler = ((Component)_Target).GetComponent<ResizeHandler>();
			if ((_ResizeHandler == null))
			{
				_ResizeHandler = ((Component)_Target).gameObject.AddComponent<ResizeHandler>();
			}
		}
	}

	private void EnsureNoResizeHandler()
	{
		if ((_ResizeHandler != null))
		{
			UnityEngine.Object.DestroyImmediate(_ResizeHandler);
		}
		_ResizeHandler = null;
	}

	private void PulseHaptic(ushort durationMicroseconds)
	{
		try
		{
			SteamVR_Controller.Device device = Device;
			if (device != null && device.connected)
				device.TriggerHapticPulse(durationMicroseconds, EVRButtonId.k_EButton_Axis0);
		}
		catch (Exception ex)
		{
			if (_hapticFailed)
				return;
			_hapticFailed = true;
			VRLog.Warn("UI pointer haptic failed: " + ex.Message);
		}
	}

	private void SettleTrigger()
	{
		_triggerSettle = true;
		_triggerLatched = false;
		_triggerHeld = false;
		_pointerChorded = false;
	}

	// One edge per physical press. Sticky OpenXR stateDown is ignored until release,
	// and a hold that is already down when the handler enables does not click.
	private bool ObserveTrigger(SteamVR_Controller.Device device, out bool fell)
	{
		bool held = device.GetPress(EVRButtonId.k_EButton_Axis1);
		bool apiDown = device.GetPressDown(EVRButtonId.k_EButton_Axis1);
		fell = false;
		if (_triggerSettle)
		{
			_triggerHeld = held;
			_triggerLatched = held;
			if (!held)
				_triggerSettle = false;
			return false;
		}

		if (_triggerHeld && !held)
			fell = true;
		if (device.GetPressUp(EVRButtonId.k_EButton_Axis1))
			fell = true;
		bool rose = false;
		if (!_triggerLatched && held && (apiDown || !_triggerHeld))
		{
			rose = true;
			_triggerLatched = true;
		}
		if (!held)
			_triggerLatched = false;
		_triggerHeld = held;
		return rose;
	}

	protected void CheckInput()
	{
		IsPressing = false;
		SteamVR_Controller.Device device = Device;
		if (LocomotionInputGate.Note(DeviceGateName(), device == null || !device.connected))
		{
			SettleTrigger();
			ReleasePointer(false);
			return;
		}

		bool fell;
		bool rose = ObserveTrigger(device, out fell);
		bool triggerPressed = _triggerHeld;
		// Grip, B/Y, or stick click own other actions. They must not also click.
		bool chord = device.GetPress(EVRButtonId.k_EButton_Grip)
			|| device.GetPress(EVRButtonId.k_EButton_ApplicationMenu)
			|| device.GetPress(EVRButtonId.k_EButton_Axis0);

		// A press can outlive the laser hit. Always release it even if the UI moved,
		// the controller lost focus, or this handler was disabled between frames.
		// A dropped press-up still completes the click; a chord that joins mid-press cancels it.
		if (_pointerDownActive)
		{
			_pointerChorded |= chord;
			IsPressing = triggerPressed && !_pointerChorded;
			if (_pointerChorded)
				ReleasePointer(false);
			else if (fell || !triggerPressed)
				ReleasePointer(true);
			return;
		}

		if (IsHittingUI && LaserVisible && (_Target != null) && !IsResizing)
		{
			if (rose && !chord)
			{
				IsPressing = true;
				_pointerChorded = false;
				VRLog.Info("UI laser trigger down");
				try
				{
					if (_hasValidHit)
					{
						SetClientCursorPosition(_lastHitUV.x, _lastHitUV.y);
						_lockedHitPoint = Laser != null
							? Laser.GetPosition(1)
							: Vector3.zero;
						_lockHit = true;
					}

					_pointerDownActive = true;
					_win32MouseDown = true;
					MouseOperations.MouseEvent(WindowsInterop.MouseEventFlags.LeftDown);

					mouseDownPosition = _hasValidHit
						? _lastHitScreenPos
						: new Vector2(Input.mousePosition.x, (float)Screen.height - Input.mousePosition.y);
					PulseHaptic(800);
				}
				catch (Exception ex)
				{
					VRLog.Warn("Failed to press VR UI pointer: " + ex.Message);
					ReleasePointer(false);
				}
			}

			if (triggerPressed && !chord)
			{
				IsPressing = true;
			}

			float thumbstickY = device.GetAxis(EVRButtonId.k_EButton_Axis0).y;
			bool scrollChord = device.GetPress(EVRButtonId.k_EButton_Grip)
				|| device.GetPress(EVRButtonId.k_EButton_ApplicationMenu);
			if (!IsPressing && !scrollChord && Mathf.Abs(thumbstickY) > 0.7f && Time.unscaledTime - _lastScrollTime > 0.05f)
			{
				try
				{
					WindowsInterop.mouse_event(0x0800, 0, 0, (int)(thumbstickY * 120f), 0);
				}
				catch (Exception ex)
				{
					if (!_scrollFailed)
					{
						_scrollFailed = true;
						VRLog.Warn("UI laser scroll failed: " + ex.Message);
					}
				}
				_lastScrollTime = Time.unscaledTime;
				if (Time.unscaledTime - _lastScrollHaptic > 0.25f)
				{
					PulseHaptic(200);
					_lastScrollHaptic = Time.unscaledTime;
				}
			}
		}
	}

	private void ReleasePointer(bool sendClick)
	{
		if (!_pointerDownActive && !_win32MouseDown)
		{
			mouseDownPosition = null;
			IsPressing = false;
			return;
		}

		try
		{
			if (_win32MouseDown)
			{
				MouseOperations.MouseEvent(WindowsInterop.MouseEventFlags.LeftUp);
			}
		}
		catch (Exception ex)
		{
			VRLog.Warn("Failed to release VR UI pointer cleanly: " + ex.Message);
		}
		finally
		{
			_pointerDownActive = false;
			_win32MouseDown = false;
			_lockHit = false;
			mouseDownPosition = null;
			IsPressing = false;
		}
	}

	private static bool _strippedGuiBoxCollider;

	// GUIQuad is a flat CreatePrimitive(Quad) with a MeshCollider on z = 0.
	// A BoxCollider has no textureCoord (always 0,0) and, when shoved to
	// local z = -0.48, sits in front of the picture the user is aiming at.
	private static void RemoveBoxColliders(GUIQuad quad)
	{
		BoxCollider[] boxes = ((Component)quad).GetComponents<BoxCollider>();
		bool removed = false;
		for (int i = 0; i < boxes.Length; i++)
		{
			BoxCollider box = boxes[i];
			if (box == null)
				continue;
			Object.Destroy(box);
			removed = true;
		}
		if (removed && !_strippedGuiBoxCollider)
		{
			_strippedGuiBoxCollider = true;
			VRLog.Info("Removed GUIQuad BoxCollider. UI laser uses the quad MeshCollider UV.");
		}
	}

	private void PrepareAim()
	{
		foreach (GUIQuad quad in GUIQuadRegistry.Quads)
		{
			if (quad != null)
				RemoveBoxColliders(quad);
		}
		Physics.SyncTransforms();
	}

	private bool TryAcquireTarget(out RaycastHit hit, out Vector2 uv)
	{
		PrepareAim();
		hit = default(RaycastHit);
		uv = _lastHitUV;
		GUIQuad best = null;
		float bestDistance = float.MaxValue;
		foreach (GUIQuad quad in GUIQuadRegistry.Quads)
		{
			if (quad == null || !((Component)quad).gameObject.activeInHierarchy)
				continue;
			Vector2 candidateUv;
			if (!IsWithinRange(quad) || !Raycast(quad, out RaycastHit candidate, out candidateUv))
				continue;
			if (candidate.distance < bestDistance)
			{
				bestDistance = candidate.distance;
				best = quad;
				hit = candidate;
				uv = candidateUv;
			}
		}
		_Target = best;
		return best != null;
	}

	private float GetRange(GUIQuad quad)
	{
		Vector3 localScale = ((Component)quad).transform.localScale;
		return Mathf.Clamp(localScale.magnitude * 2.0f, 1.5f, 5.0f);
	}

	// Directly use Laser.transform — identical to VRGIN MenuHandler
	private bool IsWithinRange(GUIQuad quad)
	{
		if (((Component)quad).transform.parent == ((Component)this).transform)
		{
			return false;
		}
		Vector3 position = ((Component)Laser).transform.position;
		Vector3 forward = ((Component)Laser).transform.forward;

		// The quad's forward vector points into the panel (away from user).
		// Laser direction must point in the general direction of the quad's front face.
		if (Vector3.Dot(((Component)quad).transform.forward, forward) <= 0f)
		{
			return false;
		}

		// Controller must not be behind the quad (in local space, front is z < 0)
		float localZ = ((Component)quad).transform.InverseTransformPoint(position).z;
		if (localZ > 0.3f)
		{
			return false;
		}

		float dist = Vector3.Distance(position, ((Component)quad).transform.position);
		return dist <= GetRange(quad) * 2f;
	}

	private bool Raycast(GUIQuad quad, out RaycastHit hit, out Vector2 uv)
	{
		RemoveBoxColliders(quad);
		hit = default(RaycastHit);
		uv = Vector2.zero;
		Vector3 origin = ((Component)Laser).transform.position;
		Vector3 direction = ((Component)Laser).transform.forward;
		if (direction.sqrMagnitude < 1e-8f)
			return false;

		// The visible quad is the transform. Its kinematic collider is often one
		// physics step behind, so a mesh hit slides off the picture after a click.
		Vector3 planePoint;
		Vector2 planeUv;
		bool planeHit = ProjectOnQuadPlane(quad, out planePoint, out planeUv);

		MeshCollider mesh = ((Component)quad).GetComponent<MeshCollider>();
		if (mesh == null)
		{
			MeshFilter mf = ((Component)quad).GetComponent<MeshFilter>();
			if (mf != null && mf.sharedMesh != null)
			{
				mesh = ((Component)quad).gameObject.AddComponent<MeshCollider>();
				mesh.sharedMesh = mf.sharedMesh;
			}
		}

		bool meshHit = false;
		if (mesh != null)
		{
			mesh.enabled = true;
			float range = GetRange(quad);
			Ray aim = new Ray(origin, direction);
			meshHit = RaycastMesh(mesh, aim, range, out hit);
		}

		if (!planeHit && !meshHit)
			return false;

		if (!meshHit)
		{
			hit.point = planePoint;
			hit.normal = -((Component)quad).transform.forward;
			hit.distance = Vector3.Distance(origin, planePoint);
			uv = planeUv;
			return true;
		}

		uv = hit.textureCoord;
		if (planeHit)
		{
			hit.point = planePoint;
			hit.distance = Vector3.Distance(origin, planePoint);
			// A missed UV read comes back as zero. The plane mapping matches the
			// unit quad the picture is drawn on.
			if (uv == Vector2.zero)
				uv = planeUv;
		}
		return true;
	}

	private bool ProjectOnQuadPlane(GUIQuad quad, out Vector3 worldPoint, out Vector2 uv)
	{
		worldPoint = Vector3.zero;
		uv = _lastHitUV;
		Transform panel = ((Component)quad).transform;
		Vector3 origin = ((Component)Laser).transform.position;
		Vector3 direction = ((Component)Laser).transform.forward;
		float range = GetRange(quad) * 1.5f;

		Plane plane = new Plane(panel.forward, panel.position);
		Ray aim = new Ray(origin, direction);
		float enter;
		if (!plane.Raycast(aim, out enter))
		{
			Plane revPlane = new Plane(-panel.forward, panel.position);
			if (!revPlane.Raycast(aim, out enter))
				return false;
		}

		if (enter < 0f || enter > range)
			return false;

		worldPoint = origin + direction * enter;
		Vector3 localPt = panel.InverseTransformPoint(worldPoint);

		if (Mathf.Abs(localPt.x) > 1.0f || Mathf.Abs(localPt.y) > 1.0f)
			return false;

		float u = Mathf.Clamp01(localPt.x + 0.5f);
		float v = Mathf.Clamp01(localPt.y + 0.5f);
		uv = new Vector2(u, v);
		return true;
	}

	private static bool RaycastMesh(MeshCollider mesh, Ray ray, float range, out RaycastHit hit)
	{
		bool previous = Physics.queriesHitBackfaces;
		Physics.queriesHitBackfaces = true;
		try
		{
			return mesh.Raycast(ray, out hit, range);
		}
		finally
		{
			Physics.queriesHitBackfaces = previous;
		}
	}

	private static void SetClientCursorPosition(float u, float v)
	{
		WindowsInterop.RECT clientRect = WindowManager.GetClientRect();
		float clientWidth = clientRect.Right - clientRect.Left;
		float clientHeight = clientRect.Bottom - clientRect.Top;
		if (clientWidth <= 0f) clientWidth = Screen.width > 0 ? Screen.width : VRGUI.Width;
		if (clientHeight <= 0f) clientHeight = Screen.height > 0 ? Screen.height : VRGUI.Height;
		if (clientWidth <= 0f) clientWidth = 1920f;
		if (clientHeight <= 0f) clientHeight = 1080f;

		int clientX = (int)(u * clientWidth);
		int clientY = (int)((1f - v) * clientHeight);

		// Strictly clamp cursor position inside the client area of the game window.
		// This guarantees that the mouse cursor can never escape to the window titlebar,
		// minimize/close buttons, window borders, or the Windows taskbar, which would
		// cause the window to minimize or lose focus on click!
		int screenX = Mathf.Clamp(clientRect.Left + clientX, clientRect.Left, Mathf.Max(clientRect.Left, clientRect.Right - 1));
		int screenY = Mathf.Clamp(clientRect.Top + clientY, clientRect.Top, Mathf.Max(clientRect.Top, clientRect.Bottom - 1));

		WindowsInterop.SetCursorPos(screenX, screenY);
	}

	// Map a quad UV (0..1) to client-pixel coordinates using the CURRENT window client size.
	private Vector2 UVToClient(float u, float v)
	{
		WindowsInterop.RECT clientRect = WindowManager.GetClientRect();
		float clientWidth = clientRect.Right - clientRect.Left;
		float clientHeight = clientRect.Bottom - clientRect.Top;
		if (clientWidth <= 0f) clientWidth = Screen.width > 0 ? Screen.width : VRGUI.Width;
		if (clientHeight <= 0f) clientHeight = Screen.height > 0 ? Screen.height : VRGUI.Height;
		if (clientWidth <= 0f) clientWidth = 1920f;
		if (clientHeight <= 0f) clientHeight = 1080f;

		return new Vector2(u * clientWidth, (1f - v) * clientHeight);
	}

	// Directly use Laser.transform — identical to VRGIN MenuHandler
	private void UpdateLaser()
	{
		Vector3 laserPos = ((Component)Laser).transform.position;
		Vector3 laserEnd = laserPos + ((Component)Laser).transform.forward;
		Laser.SetPosition(0, laserPos);
		Laser.SetPosition(1, laserEnd);

		bool hitUI;
		RaycastHit hit;
		// Hold the press point. A trigger pull moves the controller, and following
		// that motion warps the cursor off the control before the click lands.
		if (_pointerDownActive && _lockHit && _Target != null && ((Component)_Target).gameObject.activeInHierarchy)
		{
			hitUI = true;
			laserEnd = _lockedHitPoint;
		}
		else if (_pointerDownActive && _Target != null && ((Component)_Target).gameObject.activeInHierarchy)
		{
			Vector2 hitUv;
			if (Raycast(_Target, out hit, out hitUv))
			{
				hitUI = true;
				laserEnd = hit.point;
				_lastHitUV = hitUv;
			}
			else if (ProjectOnQuadPlane(_Target, out laserEnd, out _lastHitUV))
			{
				hitUI = true;
			}
			else
			{
				hitUI = false;
			}
		}
		else
		{
			Vector2 hitUv;
			hitUI = TryAcquireTarget(out hit, out hitUv);
			if (hitUI)
			{
				laserEnd = hit.point;
				_lastHitUV = hitUv;
			}
		}

		IsHittingUI = hitUI;
		LaserVisible = hitUI;
		_hasValidHit = hitUI;

		if (hitUI)
		{
			Laser.SetPosition(1, laserEnd);

			_lastHitScreenPos = UVToClient(_lastHitUV.x, _lastHitUV.y);

			bool stealFocus = false;
			if (ActiveMouseHandler == null || ActiveMouseHandler == this || !ActiveMouseHandler.LaserVisible)
			{
				stealFocus = true;
			}
			else if (ActiveMouseHandler.IsPressing == false && Device != null && Device.GetPress(EVRButtonId.k_EButton_Axis1))
			{
				stealFocus = true;
			}

			if (!IsOtherWorkingOn(_Target) && stealFocus)
			{
				ActiveMouseHandler = this;
				SetClientCursorPosition(_lastHitUV.x, _lastHitUV.y);
			}
		}

		Color targetColor = Color.cyan;

		if (LaserVisible && dotCursor != null)
		{
			if (hitUI && _Target != null)
			{
				dotCursor.SetActive(true);
				Vector3 surfaceNormal = -_Target.transform.forward;
				dotCursor.transform.position = laserEnd + surfaceNormal * 0.001f;
				dotCursor.transform.rotation = Quaternion.LookRotation(surfaceNormal, _Target.transform.up);
				float cursorScale = IsPressing ? 0.007f : 0.009f;
				dotCursor.transform.localScale = new Vector3(cursorScale, cursorScale, 1f);

				if (IsPressing)
				{
					targetColor = new Color(1f, 0.3f, 0.2f, 1f);
					Vector3 dir = laserEnd - laserPos;
					if (dir.magnitude > 0.02f)
					{
						Laser.SetPosition(1, laserEnd - dir.normalized * 0.01f);
					}
				}
				else
				{
					targetColor = new Color(0.1f, 0.9f, 1f, 0.95f);
				}
			}
			else
			{
				dotCursor.SetActive(false);
			}
		}
		else if (dotCursor != null)
		{
			dotCursor.SetActive(false);
		}

		_currentLaserColor = Color.Lerp(_currentLaserColor, targetColor, Time.deltaTime * 15f);
		_currentDotColor = _currentLaserColor;
		Laser.SetColors(_currentLaserColor, _currentLaserColor);
		if (_dotMaterial != null)
			_dotMaterial.color = _currentDotColor;

		if (hitUI)
		{
			float w = 0.002f + 0.001f * Mathf.Sin(Time.time * 3f);
			Laser.SetWidth(w, w * 0.5f);
		}
		else
		{
			Laser.SetWidth(0.002f, 0.002f);
		}
	}

	private bool IsOtherWorkingOn(GUIQuad target)
	{
		return false;
	}
}
