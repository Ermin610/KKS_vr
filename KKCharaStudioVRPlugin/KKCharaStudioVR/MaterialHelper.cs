using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KKCharaStudioVR;

internal class MaterialHelper
{
	private static AssetBundle _GripMovePluginResources;

	private static Shader _ColorZOrderShader;

	public static Shader GetColorZOrderShader()
	{
		if (_ColorZOrderShader != null)
		{
			return _ColorZOrderShader;
		}
		try
		{
			if (_GripMovePluginResources == null)
			{
#if KKS
				using (var stream = typeof(MaterialHelper).Assembly.GetManifestResourceStream("KKS.ColorZOrderShader"))
				using (var memory = new System.IO.MemoryStream())
				{
					stream.CopyTo(memory);
					_GripMovePluginResources = AssetBundle.LoadFromMemory(memory.ToArray());
				}
#else
				_GripMovePluginResources = AssetBundle.LoadFromMemory(Resource.kkcharastudiovrshader);
#endif
			}
			_ColorZOrderShader = _GripMovePluginResources.LoadAsset<Shader>("ColorZOrder");
			return _ColorZOrderShader;
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.ToString());
			return null;
		}
	}
}
