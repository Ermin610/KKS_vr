using System;
using UnityEngine;
using VRGIN.Core;
using Object = UnityEngine.Object;

namespace KKCharaStudioVR;

public class GUIUtils
{
	private static bool isVR;

	private static Texture2D windowBG;

	private static GUIStyle cachedWindowStyle;

	private static GUISkin cachedSkin;

	static GUIUtils()
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Expected O, but got Unknown
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		isVR = false;
		windowBG = new Texture2D(1, 1, (TextureFormat)5, false);
		if ((Environment.CommandLine.Contains("--vr") || Environment.CommandLine.Contains("--studiovr")) && !Environment.CommandLine.Contains("--novr"))
		{
			isVR = true;
		}
		windowBG.SetPixel(0, 0, Color.black);
		windowBG.Apply();
	}

	public static GUIStyle GetWindowStyle()
	{
		bool vr = isVR;
		try
		{
			vr = vr || VR.Active;
		}
		catch (Exception)
		{
		}
		if (!vr || GUI.skin == null)
			return GUI.skin != null ? new GUIStyle(GUI.skin.window) : GUIStyle.none;

		GUI.backgroundColor = Color.black;
		if (cachedWindowStyle != null && cachedSkin == GUI.skin)
			return cachedWindowStyle;

		GUIStyle val = new GUIStyle(GUI.skin.window);
		val.onNormal.background = windowBG;
		val.normal.background = windowBG;
		val.hover.background = windowBG;
		val.focused.background = windowBG;
		val.active.background = windowBG;
		val.hover.textColor = Color.blue;
		val.onHover.textColor = Color.blue;
		cachedWindowStyle = val;
		cachedSkin = GUI.skin;
		return val;
	}
}
