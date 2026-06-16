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

		if (_appliedMaxFps != EngineSettings.MaxFps)
		{
			_appliedMaxFps = EngineSettings.MaxFps;
			global::Godot.Engine.MaxFps = EngineSettings.MaxFps;
			Lumora.Core.Logging.Logger.Log($"Settings: fps limit -> {(EngineSettings.MaxFps == 0 ? "off" : EngineSettings.MaxFps.ToString())}");
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
	}
}
