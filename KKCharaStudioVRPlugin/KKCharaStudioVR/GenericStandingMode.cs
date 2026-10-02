using System;
using System.Collections.Generic;
using System.Linq;
using VRGIN.Controls;
using VRGIN.Controls.Tools;
using VRGIN.Core;
using VRGIN.Helpers;
using VRGIN.Modes;

namespace KKCharaStudioVR;

internal class GenericStandingMode : StandingMode
{
#if KKS
	protected override VRGIN.Controls.Controller CreateLeftController()
	{
		var controller = base.CreateLeftController();
		controller.gameObject.AddComponent<KksTrackedObject>();
		return controller;
	}
	protected override VRGIN.Controls.Controller CreateRightController()
	{
		var controller = base.CreateRightController();
		controller.gameObject.AddComponent<KksTrackedObject>();
		return controller;
	}
#endif
	public override IEnumerable<Type> Tools => new Type[1]
	{
		typeof(GripMoveKKCharaStudioTool)
	};

	protected override IEnumerable<IShortcut> CreateShortcuts()
	{
		return base.CreateShortcuts().Concat(new IShortcut[1]
		{
			new MultiKeyboardShortcut(new KeyStroke("Ctrl+C"), new KeyStroke("Ctrl+C"), delegate
			{
				VR.Manager.SetMode<GenericSeatedMode>();
			})
		});
	}
}
