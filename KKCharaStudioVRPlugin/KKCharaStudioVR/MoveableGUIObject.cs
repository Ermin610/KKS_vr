using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using VRGIN.Core;
using Object = UnityEngine.Object;

namespace KKCharaStudioVR;

public class MoveableGUIObject : MonoBehaviour
{
	public List<Action<MonoBehaviour>> onMoveLister = new List<Action<MonoBehaviour>>();

	public List<Action<MonoBehaviour>> onReleasedLister = new List<Action<MonoBehaviour>>();

	public GuideObject guideObject;

	public GuideScale guideScale;

	public ObjectCtrlInfo objectCtrlInfo;

	public Vector3 oldPos;

	public Vector3 oldRot;

	public Vector3 oldScale;

	public bool isMoveObj;

	private Renderer renderer;

	public Renderer visibleReference;

	private SphereCollider[] _spheres;

	private void Start()
	{
		renderer = ((Component)this).GetComponent<Renderer>();
		_spheres = ((Component)this).GetComponents<SphereCollider>();
	}

	public void OnMoveStart()
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		if (guideObject != null && guideObject.changeAmount != null)
		{
			oldPos = guideObject.changeAmount.pos;
			oldRot = guideObject.changeAmount.rot;
			oldScale = guideObject.changeAmount.scale;
		}
	}

	public void OnMoved()
	{
		foreach (Action<MonoBehaviour> item in onMoveLister)
		{
			try
			{
				item((MonoBehaviour)(object)this);
			}
			catch
			{
			}
		}
	}

	public void OnReleased()
	{
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0128: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Expected O, but got Unknown
		//IL_013a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Expected O, but got Unknown
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected O, but got Unknown
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Expected O, but got Unknown
		// A guide whose character was deleted mid-grab has no change amount or
		// undo manager any more. The release listeners below must still run.
		try
		{
			PushUndoCommands();
		}
		catch (Exception ex)
		{
			VRLog.Warn("Guide release undo skipped: " + ex.Message);
		}
		foreach (Action<MonoBehaviour> item in onReleasedLister)
		{
			try
			{
				item((MonoBehaviour)(object)this);
			}
			catch
			{
			}
		}
	}

	private void PushUndoCommands()
	{
		if (guideObject != null && guideObject.changeAmount != null && Singleton<UndoRedoManager>.Instance != null)
		{
			if (guideScale == null)
			{
				if (guideObject.enablePos)
				{
					GuideCommand.EqualsInfo val = new GuideCommand.EqualsInfo
					{
						dicKey = guideObject.dicKey,
						oldValue = oldPos,
						newValue = guideObject.changeAmount.pos
					};
					Singleton<UndoRedoManager>.Instance.Push((ICommand)new GuideCommand.MoveEqualsCommand((GuideCommand.EqualsInfo[])(object)new GuideCommand.EqualsInfo[1] { val }));
				}
				if (guideObject.enableRot)
				{
					GuideCommand.EqualsInfo val2 = new GuideCommand.EqualsInfo
					{
						dicKey = guideObject.dicKey,
						oldValue = oldRot,
						newValue = guideObject.changeAmount.rot
					};
					Singleton<UndoRedoManager>.Instance.Push((ICommand)new GuideCommand.RotationEqualsCommand((GuideCommand.EqualsInfo[])(object)new GuideCommand.EqualsInfo[1] { val2 }));
				}
			}
			else if (guideObject.enableScale)
			{
				GuideCommand.EqualsInfo[] array = (GuideCommand.EqualsInfo[])(object)new GuideCommand.EqualsInfo[1]
				{
					new GuideCommand.EqualsInfo
					{
						dicKey = guideObject.dicKey,
						oldValue = oldScale,
						newValue = guideObject.changeAmount.scale
					}
				};
				Singleton<UndoRedoManager>.Instance.Push((ICommand)new GuideCommand.ScaleEqualsCommand(array));
			}
		}
	}

	private void Update()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		// optionSystem is a static that can be unset while Studio is loading or shutting down.
		float manipulateSize = Studio.Studio.optionSystem != null ? Studio.Studio.optionSystem.manipulateSize : 1f;
		if (isMoveObj)
		{
			((Component)this).transform.localScale = Vector3.one * 0.1f * manipulateSize;
		}
		if (guideScale != null)
		{
			((Component)this).transform.localScale = Vector3.one * 0.05f * manipulateSize;
		}
		FitGrabCollider();
		if (guideObject != null)
		{
			bool show = VRStudioInteractionPolicy.GuideRendererEnabled(
				VRStudioInteractionPolicy.IkRendererEnabled(VRQuickActions.ikVisible),
				guideObject.visible,
				guideObject.visibleOutside);
			if (renderer != null)
				renderer.enabled = show;
			// visibleReference is the guide's own green ball (or its XYZ arrow).
			// It is what the player actually sees, so the switch has to reach it.
			if (visibleReference != null)
				visibleReference.enabled = show;
			if (_spheres != null)
			{
				bool colliders = VRStudioInteractionPolicy.IkColliderEnabled(VRQuickActions.ikVisible);
				for (int i = 0; i < _spheres.Length; i++)
				{
					if (_spheres[i] != null)
						_spheres[i].enabled = colliders;
				}
			}
		}
		if (visibleReference != null && renderer != null)
		{
			((Component)renderer).gameObject.layer = ((Component)visibleReference).gameObject.layer;
		}
	}

	private void FitGrabCollider()
	{
		if (guideObject == null || _spheres == null || _spheres.Length == 0) return;
		float lossy = Mathf.Abs(((Component)this).transform.lossyScale.x);
		if (lossy < 0.0001f) return;
		// Keep the trigger near the visible handle. A default radius of 0.5
		// swallows neighboring bones when the guide is not scaled down.
		float worldRadius = isMoveObj ? 0.05f : 0.022f;
		float radius = worldRadius / lossy;
		for (int i = 0; i < _spheres.Length; i++)
		{
			SphereCollider sphere = _spheres[i];
			if (sphere != null && Mathf.Abs(sphere.radius - radius) > 0.001f)
				sphere.radius = radius;
		}
	}
}
