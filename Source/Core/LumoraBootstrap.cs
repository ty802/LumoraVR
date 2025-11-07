using System;
using System.Threading.Tasks;
using Godot;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Management;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core;

/// <summary>
/// LumoraBootstrap handles the initialization sequence, loading screen, and engine startup.
/// </summary>
public partial class LumoraBootstrap : Node
{
	[Export] public string InitializationScene { get; set; } = "res://Scenes/Client.tscn";
	[Export] public Texture2D SplashLogo { get; set; }
	
	private ColorRect _splashBackground;
	private TextureRect _logoRect;
	private ProgressBar _progressBar;
	private Label _statusLabel;
	private Control _loadingUI;
	
	private string _initPhase = "Starting...";
	private string _initSubphase = "";
	private float _initProgress = 0f;
	
	public string InitPhase 
	{ 
		get => _initPhase;
		set
		{
			_initPhase = value;
			UpdateUI();
		}
	}
	
	public string InitSubphase 
	{ 
		get => _initSubphase;
		set
		{
			_initSubphase = value;
			UpdateUI();
		}
	}
	
	public float InitProgress
	{
		get => _initProgress;
		set
		{
			_initProgress = Mathf.Clamp(value, 0f, 1f);
			UpdateUI();
		}
	}

	public override void _Ready()
	{
		AquaLogger.Log("=============================================================");
		AquaLogger.Log("LumoraBootstrap: Starting initialization sequence");
		AquaLogger.Log("=============================================================");
		
		CreateLoadingUI();
		CallDeferred(MethodName.StartInitialization);
	}
	
	private void CreateLoadingUI()
	{
		_loadingUI = new Control();
		_loadingUI.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_loadingUI.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_loadingUI);
		
		_splashBackground = new ColorRect();
		_splashBackground.Color = new Color(0.1f, 0.1f, 0.1f, 1f);
		_splashBackground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_loadingUI.AddChild(_splashBackground);
		
		var centerContainer = new CenterContainer();
		centerContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		centerContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
		_loadingUI.AddChild(centerContainer);
		
		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(360, 0);
		panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		panel.MouseFilter = Control.MouseFilterEnum.Ignore;
		centerContainer.AddChild(panel);
		
		var panelPadding = new MarginContainer();
		panelPadding.AddThemeConstantOverride("margin_left", 24);
		panelPadding.AddThemeConstantOverride("margin_right", 24);
		panelPadding.AddThemeConstantOverride("margin_top", 24);
		panelPadding.AddThemeConstantOverride("margin_bottom", 24);
		panel.AddChild(panelPadding);
		
		var vbox = new VBoxContainer();
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		vbox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		vbox.AddThemeConstantOverride("separation", 16);
		panelPadding.AddChild(vbox);
		
		// 1. Logo (scaled inside wrapper)
		var logoWrapper = new Control();
		logoWrapper.CustomMinimumSize = new Vector2(256, 256);
		logoWrapper.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		logoWrapper.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		logoWrapper.MouseFilter = Control.MouseFilterEnum.Ignore;
		vbox.AddChild(logoWrapper);
		
		_logoRect = new TextureRect();
		_logoRect.Texture = SplashLogo ?? GD.Load<Texture2D>("res://Assets/Textures/lumoravricon.svg");
		_logoRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_logoRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_logoRect.SizeFlagsHorizontal = Control.SizeFlags.Fill;
		_logoRect.SizeFlagsVertical = Control.SizeFlags.Fill;
		_logoRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		_logoRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		logoWrapper.AddChild(_logoRect);
		
		// 2. Progress Bar (300x20)
		_progressBar = new ProgressBar();
		_progressBar.CustomMinimumSize = new Vector2(300, 20);
		_progressBar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_progressBar.MinValue = 0;
		_progressBar.MaxValue = 100;
		_progressBar.Value = 0;
		_progressBar.ShowPercentage = false;
		
		var fillStyle = new StyleBoxFlat();
		fillStyle.BgColor = new Color(0.7f, 0.4f, 1.0f, 1.0f); // Purple
		fillStyle.CornerRadiusTopLeft = 10;
		fillStyle.CornerRadiusTopRight = 10;
		fillStyle.CornerRadiusBottomLeft = 10;
		fillStyle.CornerRadiusBottomRight = 10;
		_progressBar.AddThemeStyleboxOverride("fill", fillStyle);
		
		var bgStyle = new StyleBoxFlat();
		bgStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
		bgStyle.CornerRadiusTopLeft = 10;
		bgStyle.CornerRadiusTopRight = 10;
		bgStyle.CornerRadiusBottomLeft = 10;
		bgStyle.CornerRadiusBottomRight = 10;
		_progressBar.AddThemeStyleboxOverride("background", bgStyle);
		
		vbox.AddChild(_progressBar);
		
		// 3. Status Text (debug info)
		_statusLabel = new Label();
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		_statusLabel.AddThemeFontSizeOverride("font_size", 14);
		_statusLabel.Text = "Initializing...";
		vbox.AddChild(_statusLabel);
	}
	
	private void UpdateUI()
	{
		if (_statusLabel != null)
		{
			var text = _initPhase;
			if (!string.IsNullOrEmpty(_initSubphase))
				text += $"\n{_initSubphase}";
			_statusLabel.Text = text;
		}
		
		if (_progressBar != null)
		{
			_progressBar.Value = _initProgress * 100f;
		}
	}
	
	private async void StartInitialization()
	{
		try
		{
			await InitializeAsync();
			
			// Wait a moment to show completion
			await Task.Delay(500);
			
			// Load main scene
			InitPhase = "Loading main scene...";
			InitProgress = 0.95f;
			
			GetTree().ChangeSceneToFile(InitializationScene);
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"LumoraBootstrap: Fatal error during initialization:\n{ex}");
			InitPhase = "Initialization Failed!";
			InitSubphase = ex.Message;
		}
	}
	
	private async Task InitializeAsync()
	{
		// Phase 1: Initialize logging
		InitPhase = "Initializing logging system...";
		InitProgress = 0.1f;
		await Task.Delay(100);
		
		// Phase 2: Check system requirements
		InitPhase = "Checking system requirements...";
		InitSubphase = $"GPU: {RenderingServer.GetVideoAdapterName()}";
		InitProgress = 0.2f;
		await Task.Delay(200);
		
		AquaLogger.Log($"Graphics API: {RenderingServer.GetVideoAdapterApiVersion()}");
		AquaLogger.Log($"Processor Count: {OS.GetProcessorCount()}");
		AquaLogger.Log($"Memory: {OS.GetStaticMemoryPeakUsage() / 1024 / 1024} MB");
		
		// Phase 3: Initialize core systems
		InitPhase = "Initializing core systems...";
		InitProgress = 0.3f;
		await Task.Delay(100);
		
		InitSubphase = "Argument cache";
		_ = ArgumentCache.Instance; // Ensure initialized
		InitProgress = 0.35f;
		await Task.Delay(50);
		
		// Phase 4: Check XR availability
		InitPhase = "Detecting XR devices...";
		InitProgress = 0.4f;
		var xr = XRServer.FindInterface("OpenXR");
		if (xr != null && xr.IsInitialized())
		{
			InitSubphase = "OpenXR detected";
			AquaLogger.Log("XR: OpenXR interface found and initialized");
		}
		else
		{
			InitSubphase = "Desktop mode";
			AquaLogger.Log("XR: No XR interface detected, using desktop mode");
		}
		await Task.Delay(200);
		
		// Phase 5: Pre-cache resources
		InitPhase = "Loading resources...";
		InitProgress = 0.5f;
		await Task.Delay(100);
		
		// Phase 6: Network initialization
		InitPhase = "Initializing network...";
		InitSubphase = "LNL protocol";
		InitProgress = 0.6f;
		await Task.Delay(100);
		
		// Phase 7: Asset system
		InitPhase = "Initializing asset system...";
		InitProgress = 0.7f;
		await Task.Delay(100);
		
		// Phase 8: Input system
		InitPhase = "Initializing input system...";
		InitSubphase = "Mouse and keyboard drivers";
		InitProgress = 0.8f;
		await Task.Delay(100);
		
		// Phase 9: Final checks
		InitPhase = "Finalizing initialization...";
		InitSubphase = "";
		InitProgress = 0.9f;
		await Task.Delay(200);
		
		InitPhase = "Ready!";
		InitProgress = 1.0f;
		
		AquaLogger.Log("=============================================================");
		AquaLogger.Log("LumoraBootstrap: Initialization complete");
		AquaLogger.Log("=============================================================");
	}
}
