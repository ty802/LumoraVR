// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Source.UI;

public partial class DashboardToggle : Node
{
	private static DashboardToggle? _instance;

	public static DashboardToggle? Instance => _instance;
	public static bool IsDashboardVisible { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		_instance = this;
		IsDashboardVisible = false;
	}

	public void ToggleDashboard()
	{
		IsDashboardVisible = false;
	}

	public void ShowDashboard()
	{
		IsDashboardVisible = false;
	}

	public void HideDashboard()
	{
		IsDashboardVisible = false;
	}

	public override void _ExitTree()
	{
		if (_instance == this)
		{
			_instance = null;
		}

		IsDashboardVisible = false;
		base._ExitTree();
	}
}
