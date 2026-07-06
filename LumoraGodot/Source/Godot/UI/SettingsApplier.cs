// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;

namespace Lumora.Source.UI;

/// <summary>
/// Applies the engine-owned settings the platform layer is responsible for:
/// vsync, frame cap, window mode, 3D render scale and the master audio bus.
/// Changes apply live for preview; persistence is explicit (the settings screen's
/// Save / the exit dialog commit via EngineSettings.Commit).
/// </summary>
public partial class SettingsApplier : Node
{
	public override void _Ready()
	{
		base._Ready();
		EngineSettings.Load();
		EngineSettings.Changed += Apply;
		Apply();
	}

	public override void _ExitTree()
	{
		EngineSettings.Changed -= Apply;
		base._ExitTree();
	}

	// Differential apply: Changed fires for every settings write (including each
	// slider tick), so only touch the subsystems whose value actually changed -
	// reassigning render scale or vsync per tick stalls the renderer.
	private bool? _appliedVsync;
	private int? _appliedMaxFps;
	private bool? _appliedFullscreen;
	private float? _appliedRenderScale;
	private float? _appliedMasterVolume;
	private float? _appliedUserHeight;
	private int? _appliedBackgroundFps;

	// When the window loses focus or is minimized the compositor stops blocking the swap, so vsync no longer throttles
	// us and the loop free-runs (the FPS graph spikes way past the vsync rate, burning GPU for a window nobody sees).
	// Engine.MaxFps is enforced regardless of focus or vsync, so while unfocused we clamp to the user's background-fps
	// setting and restore their normal limit when focus returns. -xlinka
	private bool _background;

	private void Apply()
	{
		if (_appliedVsync != EngineSettings.VSync)
		{
			_appliedVsync = EngineSettings.VSync;
			DisplayServer.WindowSetVsyncMode(EngineSettings.VSync
				? DisplayServer.VSyncMode.Enabled
				: DisplayServer.VSyncMode.Disabled);
			Lumora.Core.Logging.Logger.Log($"Settings: vsync -> {(EngineSettings.VSync ? "on" : "off")}");
		}

		if (_appliedMaxFps != EngineSettings.MaxFps || _appliedBackgroundFps != EngineSettings.BackgroundFps)
		{
			_appliedMaxFps = EngineSettings.MaxFps;
			_appliedBackgroundFps = EngineSettings.BackgroundFps;
			ApplyEffectiveFps();
			Lumora.Core.Logging.Logger.Log($"Settings: fps limit -> {(EngineSettings.MaxFps == 0 ? "off" : EngineSettings.MaxFps.ToString())} (background {(EngineSettings.BackgroundFps == 0 ? "off" : EngineSettings.BackgroundFps.ToString())})");
		}

		if (_appliedFullscreen != EngineSettings.Fullscreen)
		{
			_appliedFullscreen = EngineSettings.Fullscreen;
			DisplayServer.WindowSetMode(EngineSettings.Fullscreen
				? DisplayServer.WindowMode.Fullscreen
				: DisplayServer.WindowMode.Windowed);
		}

		if (_appliedRenderScale != EngineSettings.RenderScale)
		{
			_appliedRenderScale = EngineSettings.RenderScale;
			var root = GetTree()?.Root;
			if (root != null)
			{
				root.Scaling3DScale = EngineSettings.RenderScale;
			}
		}

		if (_appliedMasterVolume != EngineSettings.MasterVolume)
		{
			_appliedMasterVolume = EngineSettings.MasterVolume;
			int masterBus = AudioServer.GetBusIndex("Master");
			if (masterBus >= 0)
			{
				AudioServer.SetBusVolumeDb(masterBus, Mathf.LinearToDb(Mathf.Max(EngineSettings.MasterVolume, 0.0001f)));
				AudioServer.SetBusMute(masterBus, EngineSettings.MasterVolume <= 0f);
			}
		}

		if (_appliedUserHeight != EngineSettings.UserHeight)
		{
			_appliedUserHeight = EngineSettings.UserHeight;
			// Feed the calibrated height to the input layer; AvatarIK.MaybeRescaleAvatar reads it and re-scales the
			// avatar so its eye height matches (live). -xlinka
			var input = Lumora.Core.Engine.Current?.InputInterface;
			if (input != null)
				input.UserHeight = EngineSettings.UserHeight;
		}
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		switch (what)
		{
			// Both the application-level and per-window focus signals fire on desktop; the transition guard in
			// SetBackground makes the duplicate harmless and covers platforms that only emit one of them. -xlinka
			case (int)NotificationApplicationFocusOut:
			case (int)NotificationWMWindowFocusOut:
				SetBackground(true);
				break;
			case (int)NotificationApplicationFocusIn:
			case (int)NotificationWMWindowFocusIn:
				SetBackground(false);
				break;
		}
	}

	private void SetBackground(bool background)
	{
		if (_background == background)
		{
			return;
		}

		_background = background;
		ApplyEffectiveFps();
	}

	// Resolves Engine.MaxFps from the user's cap and the current focus state. While unfocused (and not in VR, where the
	// headset compositor owns frame timing) we clamp to the user's background-fps setting; a background setting of 0
	// disables the throttle. We never raise the rate above the user's own cap - a cap of 0 means "unlimited
	// (vsync-throttled)", which is exactly the case the background clamp rescues. -xlinka
	private void ApplyEffectiveFps()
	{
		int userCap = EngineSettings.MaxFps;
		int effective = userCap;

		if (_background && !IsVrActive())
		{
			int backgroundCap = EngineSettings.BackgroundFps;
			if (backgroundCap > 0)
			{
				effective = userCap == 0 ? backgroundCap : System.Math.Min(userCap, backgroundCap);
			}
		}

		global::Godot.Engine.MaxFps = effective;
	}

	private static bool IsVrActive()
		=> XRServer.FindInterface("OpenXR")?.IsInitialized() == true;
}
