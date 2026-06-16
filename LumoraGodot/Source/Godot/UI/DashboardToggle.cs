// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Godot.Hooks;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.UI;

public partial class DashboardToggle : Node
{
	private static DashboardToggle? _instance;
	private UserspaceDashboard _dashboard = null!;

	public static DashboardToggle? Instance => _instance;
	public static bool IsDashboardVisible { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		_instance = this;
		IsDashboardVisible = false;
		AddChild(new SettingsApplier { Name = "SettingsApplier" });
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
		{
			if (TryGetDashboard(out var dashboard) && dashboard.IsOpen.Value)
			{
				float delta = 0f;
				if (mouseBtn.ButtonIndex == MouseButton.WheelUp) delta = 1f;
				else if (mouseBtn.ButtonIndex == MouseButton.WheelDown) delta = -1f;
				if (delta != 0f)
				{
					if (dashboard.FeedAxis(new Lumora.Core.Math.float2(0f, delta)))
						GetViewport()?.SetInputAsHandled();
					return;
				}
			}
		}

		if (@event is not InputEventKey key || !key.Pressed)
			return;

		if (key.Keycode == Key.Escape)
		{
			if (TryGetDashboard(out var escDash) && escDash.IsOpen.Value && escDash.FeedEscape())
			{
				GetViewport()?.SetInputAsHandled();
				return;
			}
			ToggleDashboard();
			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (TryGetDashboard(out var dash) && dash.IsOpen.Value)
		{
			if (key.Keycode == Key.Backspace)
			{
				dash.FeedSearchBackspace();
				GetViewport()?.SetInputAsHandled();
				return;
			}
			if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
			{
				if (dash.FeedEnter())
					GetViewport()?.SetInputAsHandled();
				return;
			}
			long ch = key.Unicode;
			if (ch >= 32 && ch < 0x10000)
			{
				dash.FeedSearchChar((char)ch);
				GetViewport()?.SetInputAsHandled();
			}
		}
	}

	public override void _Process(double delta)
	{
		IsDashboardVisible = TryGetDashboard(out var dashboard) && dashboard.IsOpen.Value;
	}

	public void ToggleDashboard()
	{
		if (!TryGetDashboard(out var dashboard))
		{
			LumoraLogger.Warn("DashboardToggle: UserspaceDashboard not found.");
			IsDashboardVisible = false;
			return;
		}

		if (dashboard.IsOpen.Value)
			HideDashboard();
		else
			ShowDashboard();
	}

	public void ShowDashboard()
	{
		if (!TryGetDashboard(out var dashboard))
		{
			LumoraLogger.Warn("DashboardToggle: Cannot show dashboard, UserspaceDashboard not found.");
			IsDashboardVisible = false;
			return;
		}

		dashboard.Open();
		IsDashboardVisible = true;
		// Mouse stays captured: it steers the hand laser over the world-space
		// dash surface, not an OS cursor over a flat blit.
	}

	public void HideDashboard()
	{
		if (TryGetDashboard(out var dashboard))
		{
			dashboard.Close();
		}

		IsDashboardVisible = false;
		global::Godot.Input.MouseMode = global::Godot.Input.MouseModeEnum.Captured;
	}

	private bool TryGetDashboard(out UserspaceDashboard dashboard)
	{
		if (_dashboard != null && !_dashboard.IsDestroyed)
		{
			dashboard = _dashboard;
			return true;
		}

		_dashboard = Lumora.Core.Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot?.GetComponentInChildren<UserspaceDashboard>(true)!;
		dashboard = _dashboard!;
		return dashboard != null && !dashboard.IsDestroyed;
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
