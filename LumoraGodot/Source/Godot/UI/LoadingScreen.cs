// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using System;
using Lumora.Core;
using Lumora.Core.Components.UI;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot.UI;

// The desktop boot scene and its headset twin share actual initialization progress. The background
// is opaque from the first frame; only the content enters, then both views fade when boot is ready.
public partial class LoadingScreen : Control
{
	private Label _statusLabel = null!;
	private Label _percentageLabel = null!;
	private ProgressBar _progressBar = null!;
	private VBoxContainer _content = null!;
	private MarginContainer _frame = null!;
	private Control _logoSpace = null!;
	private TextureRect _logo = null!;
	private Panel _activity = null!;
	private StyleBoxFlat _activityStyle = null!;
	private VrLoadingEnvironment? _vrEnvironment;
	private Tween? _entrance;
	private Tween? _exit;

	private float _targetProgress;
	private float _currentProgress;
	private string _status = PhaseMessages.EngineCoreInit;
	private double _clock;
	private bool _dismissing;
	private bool _failed;
	private int _shownPercentage = -1;
	private const float ProgressSpeed = 200f;

	public override void _Ready()
	{
		Visible = true;
		Modulate = Colors.White;
		_statusLabel = GetNode<Label>("%StatusLabel");
		_percentageLabel = GetNode<Label>("%PercentageLabel");
		_progressBar = GetNode<ProgressBar>("%ProgressBar");
		_content = GetNode<VBoxContainer>("%Content");
		_frame = GetNode<MarginContainer>("%Frame");
		_logoSpace = GetNode<Control>("%LogoSpace");
		_logo = GetNode<TextureRect>("%Logo");
		_activity = GetNode<Panel>("%Activity");
		_activityStyle = (StyleBoxFlat)_activity.GetThemeStylebox("panel");

		GetNode<Label>("%VersionLabel").Text = BuildInfo.Version == "0.0.0-dev"
			? "Development build" : $"v{BuildInfo.Version}";
		_statusLabel.Text = _status;
		ApplyTheme();
		UpdateProgressDisplay();
		Resized += ApplyLayout;
		ApplyLayout();

		_content.Modulate = new Color(1f, 1f, 1f, 0f);
		_entrance = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		_entrance.TweenProperty(_content, "modulate:a", 1f, 0.45);
	}

	// DashTheme stores linear colors for the engine's UI meshes. Godot Controls take sRGB colors.
	internal static Color ThemeColor(in Lumora.Core.Math.color value)
	{
		var srgb = DashTheme.MeshTint(value);
		return new Color(srgb.r, srgb.g, srgb.b, srgb.a);
	}

	private void ApplyTheme()
	{
		AddThemeColorOverride("font_color", ThemeColor(DashTheme.Text));
		_statusLabel.AddThemeColorOverride("font_color", ThemeColor(DashTheme.Text));
		GetNode<Label>("%Title").AddThemeColorOverride("font_color", ThemeColor(DashTheme.Text));
		_percentageLabel.AddThemeColorOverride("font_color", ThemeColor(DashTheme.TextDim));
		foreach (var name in new[] { "%EngineLabel", "%VersionLabel" })
			GetNode<Label>(name).AddThemeColorOverride("font_color", ThemeColor(DashTheme.TextMuted));
		((StyleBoxFlat)_progressBar.GetThemeStylebox("background")).BgColor = ThemeColor(DashTheme.Field);
		((StyleBoxFlat)_progressBar.GetThemeStylebox("fill")).BgColor = ThemeColor(DashTheme.Accent);
		var backdrop = ThemeColor(DashTheme.Backdrop);
		GetNode<ColorRect>("%Background").Color = new Color(backdrop, 1f);
		_activityStyle.BgColor = ThemeColor(DashTheme.Accent);
	}

	private void ApplyLayout()
	{
		if (_content == null) return;
		bool compact = Size.Y < 580f;
		bool shortWindow = Size.Y < 380f;
		int inset = Size.X < 520f || compact ? 16 : 32;
		_frame.AddThemeConstantOverride("margin_left", inset);
		_frame.AddThemeConstantOverride("margin_right", inset);
		_frame.AddThemeConstantOverride("margin_top", compact ? 12 : 24);
		_frame.AddThemeConstantOverride("margin_bottom", compact ? 12 : 24);
		_content.CustomMinimumSize = new Vector2(Mathf.Max(0f, Mathf.Min(360f, Size.X - inset * 2f)), 0f);
		_content.AddThemeConstantOverride("separation", compact ? 24 : 36);
		_logoSpace.Visible = !shortWindow;
		_logoSpace.CustomMinimumSize = new Vector2(0f, compact ? 100f : 160f);
		float logoSize = compact ? 88f : 144f;
		_logo.OffsetLeft = _logo.OffsetTop = -logoSize * 0.5f;
		_logo.OffsetRight = _logo.OffsetBottom = logoSize * 0.5f;
		_logo.PivotOffset = Vector2.One * logoSize * 0.5f;
		GetNode<Label>("%Title").AddThemeFontSizeOverride("font_size", compact ? 32 : 40);
		GetNode<Control>("%Footer").Visible = !shortWindow;
		GetNode<Label>("%EngineLabel").Visible = Size.X >= 420f;
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (_currentProgress < _targetProgress)
		{
			_currentProgress = Mathf.MoveToward(_currentProgress, _targetProgress, (float)delta * ProgressSpeed);
			UpdateProgressDisplay();
		}

		// Keep a stalled loading phase visibly alive without inventing additional progress.
		bool complete = _currentProgress >= 100f || _failed;
		_activity.Modulate = new Color(1f, 1f, 1f, complete ? 1f : 0.65f + 0.35f * MathF.Sin((float)_clock * 2.6f));
		_logo.Scale = Vector2.One * (1f + 0.012f * MathF.Sin((float)_clock * 1.2f));
	}

	// Grows the headset twin. Called by the runner the moment the XR viewport is configured, which is
	// mid-phase-2, so the twin is seeded with whatever the 2D screen already shows. Returns false and
	// leaves the 2D screen alone when XR is not actually presenting; both may coexist (PCVR shows the
	// Control on the monitor and the environment in the headset). -xlinka
	public bool AttachVrEnvironment(Camera3D? xrCamera, Viewport? xrViewport)
	{
		if (_dismissing || IsQueuedForDeletion())
			return false;
		if (_vrEnvironment != null && GodotObject.IsInstanceValid(_vrEnvironment))
			return true;

		if (xrCamera == null || xrViewport == null)
		{
			LumoraLogger.Warn("LoadingScreen: VR environment skipped - no XR camera or viewport to follow.");
			return false;
		}

		var primary = XRServer.PrimaryInterface;
		if (!xrViewport.UseXR || primary == null || !primary.IsInitialized())
		{
			LumoraLogger.Debug($"LoadingScreen: VR environment skipped - XR viewport not presenting (UseXR={xrViewport.UseXR}, primary={(primary == null ? "none" : primary.GetName())}).");
			return false;
		}

		try
		{
			_vrEnvironment = new VrLoadingEnvironment { Name = "VrLoadingEnvironment" };
			_vrEnvironment.Bind(xrCamera, xrViewport);
			_vrEnvironment.SetStatus(_status);
			_vrEnvironment.SetProgress(_targetProgress);
			_vrEnvironment.SnapProgress(_currentProgress);
			AddChild(_vrEnvironment);
			LumoraLogger.Debug($"LoadingScreen: VR environment attached to '{xrCamera.Name}' in viewport '{xrViewport.Name}' (UseXR={xrViewport.UseXR}).");
			return true;
		}
		catch (Exception ex)
		{
			LumoraLogger.Warn($"LoadingScreen: VR environment failed to attach ({ex.Message}); headset stays on the bare world.");
			_vrEnvironment?.QueueFree();
			_vrEnvironment = null;
			return false;
		}
	}

	public void SetProgress(float percentage)
	{
		if (_dismissing || _failed || !float.IsFinite(percentage)) return;
		_targetProgress = Mathf.Max(_targetProgress, Mathf.Clamp(percentage, 0f, 100f));
		_vrEnvironment?.SetProgress(_targetProgress);
	}

	public void SetStatus(string status)
	{
		if (_dismissing) return;
		_status = status ?? string.Empty;
		if (_statusLabel != null) _statusLabel.Text = _status;
		_vrEnvironment?.SetStatus(_status);
	}

	public void SetPhase(int phaseNumber, int totalPhases, string phaseName)
	{
		if (_dismissing || _failed) return;
		if (totalPhases > 0)
		{
			int phase = Math.Clamp(phaseNumber, 1, totalPhases);
			// A phase is just starting here; do not report its work as already complete.
			SetProgress((phase - 1f) / totalPhases * 100f);
		}
		SetStatus(phaseName);
	}

	public void ShowError(string message)
	{
		if (_dismissing) return;
		SetStatus(message);
		_failed = true;
		_targetProgress = _currentProgress;
		if (_activityStyle != null)
		{
			_activityStyle.BgColor = ThemeColor(DashTheme.Negative);
			((StyleBoxFlat)_progressBar.GetThemeStylebox("fill")).BgColor = ThemeColor(DashTheme.Negative);
		}
		_vrEnvironment?.ShowError(message);
	}

	// Idempotent: callbacks arriving during the transition cannot restart it or touch freed UI.
	public void Dismiss()
	{
		if (_dismissing || IsQueuedForDeletion()) return;
		_dismissing = true;
		_entrance?.Kill();
		_exit = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_exit.TweenMethod(Callable.From<float>(SetOpacity), 1f, 0f, 0.32);
		_exit.TweenCallback(Callable.From(() =>
		{
			_vrEnvironment?.Dismiss();
			_vrEnvironment = null;
			Visible = false;
			QueueFree();
		}));
	}

	private void SetOpacity(float opacity)
	{
		Modulate = new Color(1f, 1f, 1f, opacity);
		_vrEnvironment?.SetOpacity(opacity);
	}

	private void UpdateProgressDisplay()
	{
		_progressBar.Value = _currentProgress;
		int percentage = _currentProgress >= 100f ? 100 : Mathf.FloorToInt(_currentProgress);
		if (percentage == _shownPercentage) return;
		_shownPercentage = percentage;
		_percentageLabel.Text = $"{percentage}%";
		if (percentage == 100)
			_activityStyle.BgColor = ThemeColor(DashTheme.Positive);
	}

	public override void _ExitTree()
	{
		Resized -= ApplyLayout;
		_entrance?.Kill();
		_exit?.Kill();
		_vrEnvironment?.Dismiss();
		_vrEnvironment = null;
	}

	public static class PhaseMessages
	{
		public const string EnvironmentSetup = "Preparing your space…";
		public const string XRDetection = "Checking your headset…";
		public const string HeadOutputCreation = "Preparing your view…";
		public const string EngineCoreInit = "Starting Lumora…";
		public const string SystemIntegration = "Connecting your devices…";
		public const string UserspaceSetup = "Getting things ready…";
		public const string Ready = "Welcome to Lumora";
		public const string ConnectingToWorld = "Connecting to world…";
		public const string WaitingForJoinGrant = "Requesting access…";
		public const string DownloadingWorldState = "Downloading world data…";
		public const string InitializingWorld = "Opening world…";
		public const string WorldReady = "World ready";
	}

	private static readonly string[] DefaultMessages =
	{
		PhaseMessages.EnvironmentSetup, PhaseMessages.XRDetection, PhaseMessages.HeadOutputCreation,
		PhaseMessages.EngineCoreInit, PhaseMessages.SystemIntegration, PhaseMessages.UserspaceSetup,
		PhaseMessages.Ready,
	};

	public void UpdatePhase(int phaseIndex, string? customMessage = null)
	{
		if (_dismissing || _failed) return;
		phaseIndex = Math.Clamp(phaseIndex, 0, DefaultMessages.Length - 1);
		string message = string.IsNullOrWhiteSpace(customMessage) ? DefaultMessages[phaseIndex] : customMessage;
		if (phaseIndex == DefaultMessages.Length - 1)
		{
			SetProgress(100f);
			SetStatus(message);
		}
		else
			SetPhase(phaseIndex + 1, DefaultMessages.Length - 1, message);
	}
}
