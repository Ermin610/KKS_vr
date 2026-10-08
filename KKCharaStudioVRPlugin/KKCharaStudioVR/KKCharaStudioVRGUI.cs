using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;
using VRUtil;
using VRGIN.Core;
using VRGIN.Visuals;

namespace KKCharaStudioVR;

public class KKCharaStudioVRGUI : MonoBehaviour
{
	private int windowID = 8731;
	private Rect windowRect = new Rect((float)(Screen.width - 250), (float)(Screen.height - 400), 250f, 10f);
	private string windowTitle = "KKCharaStudioVR Settings";

	private bool _desktopCoverEnabled;
	private Coroutine _coverRoutine;
	private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
	private GUIStyle _buttonStyle;
	private GUIStyle _labelStyle;
	private GUIStyle _toggleStyle;
	private GUIStyle _headerStyle;
	private GUISkin _styledSkin;
	private bool _settingsGuiVisible;

	private void Start()
	{
		// Privacy mode is a saved setting, so it survives closing Studio.
		KKCharaStudioVRSettings settings = GetSettings();
		bool enabled = VRDesktopCoverPolicy.ResolveInitialState(
			settings != null,
			settings != null && settings.DesktopCoverEnabled);
		SetDesktopCover(enabled, false);
		VRLog.Info("Desktop cover restored from settings: " + (enabled ? "on" : "off"));
	}

	private void Update()
	{
		if (VRDesktopCoverPolicy.ShouldToggleFromKey(Input.GetKeyDown(KeyCode.Space), IsTextInputFocused()))
		{
			SetDesktopCover(!_desktopCoverEnabled, true);
		}
	}

	private void OnDestroy()
	{
		if (_coverRoutine != null)
		{
			StopCoroutine(_coverRoutine);
			_coverRoutine = null;
		}
	}

	private static KKCharaStudioVRSettings GetSettings()
	{
		try
		{
			return VR.Manager != null && VR.Manager.Context != null
				? VR.Manager.Context.Settings as KKCharaStudioVRSettings
				: null;
		}
		catch
		{
			return null;
		}
	}

	private static bool IsTextInputFocused()
	{
		UnityEngine.EventSystems.EventSystem eventSystem = UnityEngine.EventSystems.EventSystem.current;
		GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
		if (selected == null)
			return false;
		UnityEngine.UI.InputField input = selected.GetComponent<UnityEngine.UI.InputField>();
		if (input != null)
			return input.isFocused;
		// TextMeshPro input fields, without a compile-time TMPro reference.
		Component[] components = selected.GetComponents<Component>();
		for (int i = 0; i < components.Length; i++)
		{
			if (components[i] != null && components[i].GetType().Name == "TMP_InputField")
				return true;
		}
		return false;
	}

	private void SetDesktopCover(bool enabled, bool persist)
	{
		_desktopCoverEnabled = enabled;

		// The cover used to be a full-screen black Camera at depth 99999. A camera
		// joins the frame's camera stack, so whatever samples the game view (the
		// floating Studio panel in some KKS setups) was blacked out with it and the
		// VR UI could not be used while privacy mode was on. Painting black at the
		// end of the frame touches only the desktop back buffer, after every eye
		// texture and GUI texture of that frame has been rendered.
		if (enabled && _coverRoutine == null)
		{
			_coverRoutine = StartCoroutine(PaintDesktopCover());
		}

		try
		{
#if KKS
			UnityEngine.XR.XRSettings.showDeviceView = VRDesktopCoverPolicy.ShowDeviceView(enabled);
#else
			UnityEngine.VR.VRSettings.showDeviceView = VRDesktopCoverPolicy.ShowDeviceView(enabled);
#endif
		}
		catch (Exception e)
		{
			VRLog.Warn($"Failed to set showDeviceView: {e.Message}");
		}

		if (!persist)
			return;
		KKCharaStudioVRSettings settings = GetSettings();
		if (settings == null)
			return;
		try
		{
			settings.DesktopCoverEnabled = enabled;
			settings.Save();
			VRLog.Info("Desktop cover " + (enabled ? "enabled" : "disabled") + " and saved");
		}
		catch (Exception e)
		{
			VRLog.Warn("Desktop cover state could not be saved: " + e.Message);
		}
	}

	private IEnumerator PaintDesktopCover()
	{
		while (_desktopCoverEnabled)
		{
			yield return EndOfFrame;
			if (!_desktopCoverEnabled)
				break;
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = null;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = previous;
		}
		_coverRoutine = null;
	}

	private void OnGUI()
	{
		// The settings panel is sampled from the IMGUI texture on the GUI quad.
		// While every quad is hidden, layout of this window is pure overhead.
		bool visible = false;
		foreach (GUIQuad quad in GUIQuadRegistry.Quads)
		{
			if (quad != null && ((Component)quad).gameObject.activeInHierarchy)
			{
				visible = true;
				break;
			}
		}
		if (visible != _settingsGuiVisible)
		{
			_settingsGuiVisible = visible;
			VRLog.Info(visible
				? "Settings IMGUI resumed"
				: "Settings IMGUI paused while GUI quads are hidden");
		}
		if (!visible)
			return;

		if (VRIMGUIUtil.VRGUISkin != null)
			GUI.skin = VRIMGUIUtil.VRGUISkin;
		EnsureStyles();
		windowRect = GUI.Window(windowID, windowRect, FuncWindowGUI, windowTitle, GUIUtils.GetWindowStyle());
	}

	private void EnsureStyles()
	{
		if (GUI.skin == null || (_styledSkin == GUI.skin && _labelStyle != null))
			return;
		_styledSkin = GUI.skin;
		_buttonStyle = new GUIStyle(GUI.skin.button);
		_buttonStyle.normal.textColor = Color.white;
		_buttonStyle.alignment = TextAnchor.MiddleCenter;
		_labelStyle = new GUIStyle(GUI.skin.label);
		_labelStyle.normal.textColor = Color.white;
		_labelStyle.alignment = TextAnchor.MiddleLeft;
		_labelStyle.wordWrap = false;
		_toggleStyle = new GUIStyle(GUI.skin.toggle);
		_toggleStyle.normal.textColor = Color.white;
		_toggleStyle.onNormal.textColor = Color.white;
		_headerStyle = new GUIStyle(_labelStyle);
		_headerStyle.fontStyle = FontStyle.Bold;
		_headerStyle.alignment = TextAnchor.MiddleCenter;
	}

	private void Label(string text)
	{
		GUILayout.Label(text, _labelStyle);
	}

	private void Header(string text)
	{
		GUILayout.Label(text, _headerStyle);
	}

	private bool Button(string text)
	{
		return GUILayout.Button(text, _buttonStyle);
	}

	private bool Toggle(bool value, string text)
	{
		return GUILayout.Toggle(value, text, _toggleStyle);
	}

	private void FuncWindowGUI(int winID)
	{
		try
		{
			if ((int)Event.current.type == 0)
			{
				GUI.FocusControl("");
				GUI.FocusWindow(winID);
			}
			GUI.enabled = true;
			EnsureStyles();

			GUILayout.BeginVertical();

			KKCharaStudioVRSettings settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
			if (settings != null)
			{
				Header("--- 移动设置 ---");
				Label($"Locomotion Speed: {settings.LocomotionSpeed:F1}");
				settings.LocomotionSpeed = GUILayout.HorizontalSlider(settings.LocomotionSpeed, 0.5f, 10f);

				Label($"Snap Turn Angle: {settings.SnapTurnAngle:F0}");
				settings.SnapTurnAngle = GUILayout.HorizontalSlider(settings.SnapTurnAngle, 15f, 180f);

				settings.SmoothTurnEnabled = Toggle(settings.SmoothTurnEnabled, "Smooth Turn Enabled");

				if (settings.SmoothTurnEnabled)
				{
					Label($"Smooth Turn Speed: {settings.SmoothTurnSpeed:F0}");
					settings.SmoothTurnSpeed = GUILayout.HorizontalSlider(settings.SmoothTurnSpeed, 30f, 180f);
				}

				GUILayout.Space(5);
				Header("--- UI 设置 ---");
				if (Button("Reset Camera Position"))
				{
					if (VRCameraMoveHelper.Instance != null)
					{
						VRCameraMoveHelper.Instance.MoveToCurrent();
					}
				}

				if (Button("Hide/Show All UI"))
				{
					VRQuickActions actions = ((Component)this).gameObject.GetComponent<VRQuickActions>();
					if (actions != null)
					{
						actions.ForceHideUI();
					}
				}

				Label($"UI Spawn Distance: {settings.UISpawnDistance:F2}m");
				settings.UISpawnDistance = GUILayout.HorizontalSlider(
					settings.UISpawnDistance,
					VRCameraMoveHelper.MinMainUIDistance,
					VRCameraMoveHelper.MaxMainUIDistance);
				Label($"Main UI Scale: {settings.UISpawnScale:F1}x");
				settings.UISpawnScale = GUILayout.HorizontalSlider(
					settings.UISpawnScale,
					VRCameraMoveHelper.MinMainUIScale,
					VRCameraMoveHelper.MaxMainUIScale);
				if (Button("Apply / Recall Main Studio GUI"))
				{
					VRQuickActions actions = ((Component)this).gameObject.GetComponent<VRQuickActions>();
					if (actions != null)
						actions.SummonMainGUI();
					else
						VRCameraMoveHelper.RepositionMainUI(settings.UISpawnDistance, settings.UISpawnScale);
				}

				Label("Face Buttons (Wrist Menu / GUI Toggle)");
				int controllerLayout = settings.ControllerFaceButtonLayout == KKCharaStudioVRSettings.ControllerLayoutLeftHand
					? 1
					: settings.ControllerFaceButtonLayout == KKCharaStudioVRSettings.ControllerLayoutRightHand
						? 2
						: 0;
				int nextControllerLayout = GUILayout.SelectionGrid(
					controllerLayout,
					new[] { "Split X/A", "Left X/Y", "Right A/B" },
					3,
					_buttonStyle);
				if (nextControllerLayout != controllerLayout)
				{
					settings.ControllerFaceButtonLayout = nextControllerLayout == 1
						? KKCharaStudioVRSettings.ControllerLayoutLeftHand
						: nextControllerLayout == 2
							? KKCharaStudioVRSettings.ControllerLayoutRightHand
							: KKCharaStudioVRSettings.ControllerLayoutSplitHands;
				}
				settings.TimelineFollowCamera = Toggle(
					settings.TimelineFollowCamera,
					"Timeline follows camera (off = animation only)");

				settings.WristMenuEnabled = Toggle(settings.WristMenuEnabled, "Wrist Quick Menu");
				if (settings.WristMenuEnabled)
				{
					Label($"Wrist Menu Scale: {settings.WristMenuScale:F2}");
					settings.WristMenuScale = GUILayout.HorizontalSlider(settings.WristMenuScale, 0.7f, 1.5f);
				}

				GUILayout.Space(5);
				Header("--- 手部设置 ---");
				settings.HandModelEnabled = Toggle(settings.HandModelEnabled, "Hand Model Enabled");
				if (settings.HandModelEnabled)
				{
					Label($"Hand Alpha: {settings.HandModelAlpha:F2}");
					settings.HandModelAlpha = GUILayout.HorizontalSlider(settings.HandModelAlpha, 0.05f, 1f);
					Label($"Hand Scale: {settings.HandModelScale:F2}");
					settings.HandModelScale = GUILayout.HorizontalSlider(settings.HandModelScale, 0.5f, 2f);

					Label($"Hand Offset X (L/R): {settings.HandOffsetX:F3}");
					settings.HandOffsetX = GUILayout.HorizontalSlider(settings.HandOffsetX, -0.2f, 0.2f);
					Label($"Hand Offset Y (U/D): {settings.HandOffsetY:F3}");
					settings.HandOffsetY = GUILayout.HorizontalSlider(settings.HandOffsetY, -0.2f, 0.2f);
					Label($"Hand Offset Z (F/B): {settings.HandOffsetZ:F3}");
					settings.HandOffsetZ = GUILayout.HorizontalSlider(settings.HandOffsetZ, -0.2f, 0.2f);

					Label($"Hand Rot Pitch (X): {settings.HandRotPitch:F0}");
					settings.HandRotPitch = GUILayout.HorizontalSlider(settings.HandRotPitch, -90f, 90f);
					Label($"Hand Rot Yaw (Y): {settings.HandRotYaw:F0}");
					settings.HandRotYaw = GUILayout.HorizontalSlider(settings.HandRotYaw, -90f, 90f);
					Label($"Hand Rot Roll (Z): {settings.HandRotRoll:F0}");
					settings.HandRotRoll = GUILayout.HorizontalSlider(settings.HandRotRoll, -90f, 90f);
				}

				GUILayout.Space(5);
				Header("--- 物理设置 ---");
				settings.PhysicsHandsEnabled = Toggle(settings.PhysicsHandsEnabled, "Physics Hands (防穿模物理手)");
				settings.DynamicBoneCollisionEnabled = Toggle(settings.DynamicBoneCollisionEnabled, "DynamicBone Collision");
				if (settings.DynamicBoneCollisionEnabled)
				{
					Label($"Collider Radius: {settings.ColliderRadius:F3}");
					settings.ColliderRadius = GUILayout.HorizontalSlider(settings.ColliderRadius, 0.005f, 0.1f);
				}

				settings.HapticFeedbackEnabled = Toggle(settings.HapticFeedbackEnabled, "Haptic Feedback");
				if (settings.HapticFeedbackEnabled)
				{
					Label($"Haptic Intensity: {settings.HapticFeedbackIntensity:F2}");
					settings.HapticFeedbackIntensity = GUILayout.HorizontalSlider(settings.HapticFeedbackIntensity, 0.1f, 1f);
					settings.VibrateOnlyOnBreasts = Toggle(settings.VibrateOnlyOnBreasts, "Only Vibrate on Breasts (仅触碰胸部时震动)");
				}

				settings.ProximityGrabEnabled = Toggle(settings.ProximityGrabEnabled, "Proximity Grab");
				if (settings.ProximityGrabEnabled)
				{
					Label($"Grab Radius: {settings.ProximityGrabRadius:F2}m");
					settings.ProximityGrabRadius = GUILayout.HorizontalSlider(settings.ProximityGrabRadius, 0.05f, 0.25f);
				}

				GUILayout.Space(5);
				Header("--- 舒适设置 ---");
				settings.ComfortVignetteEnabled = Toggle(settings.ComfortVignetteEnabled, "Movement Vignette");
				if (settings.ComfortVignetteEnabled)
				{
					Label($"Vignette Radius: {settings.ComfortVignetteRadius:F2}");
					settings.ComfortVignetteRadius = GUILayout.HorizontalSlider(settings.ComfortVignetteRadius, 0.3f, 0.8f);
				}

				GUILayout.Space(5);
				Header("--- 高级设置 ---");
				settings.TwoHandScaleEnabled = Toggle(settings.TwoHandScaleEnabled, "Two-Hand World Scale");

				if (Button(VRDesktopCoverPolicy.ButtonLabel(_desktopCoverEnabled)))
				{
					SetDesktopCover(!_desktopCoverEnabled, true);
				}

				GUILayout.Space(10);
				GUILayout.BeginHorizontal();
				if (Button("Save Settings"))
				{
					settings.Save();
				}
				if (Button("Reset to Default"))
				{
					settings.LocomotionSpeed = 2.0f;
					settings.SnapTurnAngle = 45f;
					settings.SnapTurnCooldown = 0.3f;
					settings.SmoothTurnEnabled = false;
					settings.SmoothTurnSpeed = 90f;
					settings.HandModelEnabled = true;
					settings.HandModelAlpha = 0.3f;
					settings.HandModelScale = 1.0f;
					settings.HandOffsetX = 0f;
					settings.HandOffsetY = -0.02f;
					settings.HandOffsetZ = -0.05f;
					settings.HandRotPitch = 30f;
					settings.HandRotYaw = 0f;
					settings.HandRotRoll = 0f;
					settings.DynamicBoneCollisionEnabled = true;
					settings.PhysicsHandsEnabled = true;
					settings.ColliderRadius = 0.02f;
					settings.HapticFeedbackEnabled = true;
					settings.HapticFeedbackIntensity = 0.5f;
					settings.VibrateOnlyOnBreasts = true;
					settings.ProximityGrabEnabled = true;
					settings.ProximityGrabRadius = 0.12f;
					settings.UISpawnDistance = KKCharaStudioVRSettings.DefaultUISpawnDistance;
					settings.UISpawnScale = KKCharaStudioVRSettings.DefaultUISpawnScale;
					settings.ControllerFaceButtonLayout = KKCharaStudioVRSettings.ControllerLayoutSplitHands;
					settings.TimelineFollowCamera = true;
					settings.ComfortVignetteEnabled = true;
					settings.ComfortVignetteRadius = 0.5f;
					settings.TwoHandScaleEnabled = true;
					settings.WristMenuEnabled = true;
					settings.WristMenuScale = 1.0f;
					settings.Save();
				}
				GUILayout.EndHorizontal();
			}

			if (Button("Close"))
			{
				VRQuickActions actions = ((Component)this).gameObject.GetComponent<VRQuickActions>();
				if (actions != null)
				{
					actions.ForceHideUI();
				}
			}

			GUILayout.EndVertical();
			GUI.DragWindow();
		}
		catch (Exception value)
		{
			Console.WriteLine(value);
		}
	}
}
