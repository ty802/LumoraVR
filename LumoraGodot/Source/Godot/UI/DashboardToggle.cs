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
	private UserspaceDashboard _dashboard;

	private CanvasLayer? _overlayLayer;
	private TextureRect? _overlayRect;
	private Vector2 _lastWindowSize = Vector2.Zero;
	private int _sizeStableFrames;
	private float _appliedAspect;

	public static DashboardToggle? Instance => _instance;
	public static bool IsDashboardVisible { get; private set; }

	public override void _Ready()
	{
		base._Ready();
		_instance = this;
		IsDashboardVisible = false;
		BuildOverlay();
	}

	private void BuildOverlay()
	{
		_overlayLayer = new CanvasLayer { Name = "DashOverlay", Layer = 100, Visible = false };
		AddChild(_overlayLayer);

		_overlayRect = new TextureRect
		{
			Name = "DashTexture",
			StretchMode = TextureRect.StretchModeEnum.Scale,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_overlayLayer.AddChild(_overlayRect);
		_overlayRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	private void UpdateOverlay(UserspaceDashboard dashboard)
	{
		if (_overlayLayer == null || _overlayRect == null)
			return;

		bool vr = Lumora.Core.Engine.Current?.InputInterface?.VR_Active ?? false;
		bool showFlat = !vr && dashboard.IsOpen.Value;

		if (showFlat)
		{
			ApplyAspectDebounced(dashboard);

			var tex = dashboard.RenderTextureSource?.Asset?.Hook as IGodotTexture;
			_overlayRect.Texture = tex?.GodotTexture2D;
		}

		bool visible = showFlat && _overlayRect.Texture != null;
		_overlayLayer.Visible = visible;

		if (visible)
			FeedPointer(dashboard);
		else
			dashboard.ClearPointer();
	}

	private void FeedPointer(UserspaceDashboard dashboard)
	{
		var win = GetViewport().GetVisibleRect().Size;
		if (win.X <= 0f || win.Y <= 0f)
			return;

		var mp = GetViewport().GetMousePosition();
		var normalized = new Lumora.Core.Math.float2(
			Mathf.Clamp(mp.X / win.X, 0f, 1f),
			Mathf.Clamp(mp.Y / win.Y, 0f, 1f));
		bool pressed = global::Godot.Input.IsMouseButtonPressed(MouseButton.Left);
		dashboard.UpdatePointer(normalized, pressed);
	}

	private void ApplyAspectDebounced(UserspaceDashboard dashboard)
	{
		var size = GetViewport().GetVisibleRect().Size;
		if (size.Y <= 0f)
			return;

		if (size != _lastWindowSize)
		{
			_lastWindowSize = size;
			_sizeStableFrames = 0;
			return;
		}

		if (_sizeStableFrames < 3)
		{
			_sizeStableFrames++;
			if (_sizeStableFrames < 3)
				return;

			float aspect = size.X / size.Y;
			if (Mathf.Abs(aspect - _appliedAspect) > 0.001f)
			{
				_appliedAspect = aspect;
				dashboard.SetAspect(aspect);
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		if (key.Keycode != Key.Escape)
			return;

		ToggleDashboard();
		GetViewport()?.SetInputAsHandled();
	}

	public override void _Process(double delta)
	{
		if (TryGetDashboard(out var dashboard))
		{
			IsDashboardVisible = dashboard.IsOpen.Value;
			UpdateOverlay(dashboard);
		}
		else if (_overlayLayer != null)
		{
			_overlayLayer.Visible = false;
		}
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
		global::Godot.Input.MouseMode = global::Godot.Input.MouseModeEnum.Visible;
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

		_dashboard = Lumora.Core.Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot?.GetComponentInChildren<UserspaceDashboard>(true);
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
