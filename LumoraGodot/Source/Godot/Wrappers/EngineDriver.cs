using System;
using Godot;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Management;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Aquamarine.Source.Godot.Wrappers;

/// <summary>
/// Godot Node wrapper for the pure C# Engine.
/// Handles platform-specific initialization and delegates to Engine.
/// </summary>
public partial class EngineDriver : Node
{
	private Lumora.Core.Engine _engine;

	[Export] public NodePath InputRootPath { get; set; }
	[Export] public bool AutoHostLocalHome { get; set; } = true;
	[Export] public bool AutoConnectLocalHome { get; set; } = true;

	private Node3D _inputRoot;
	private XRInterface _xrInterface;
	// TODO: IInputProvider deleted - VR input needs redesign with new InputInterface system
	// private IInputProvider _inputProvider;
	// TODO: UserSpaceManager deleted - needs platform-agnostic redesign
	// private UserSpaceManager _userSpaceManager;

	public override void _Ready()
	{
		AquaLogger.Log("EngineDriver: Initializing Godot platform layer...");

		// Initialize Godot-specific systems
		InitializeInputRoot();
		InitializeInput();
		InitializeManagers();

		// Start async initialization
		CallDeferred(MethodName.InitializeEngineAsync);
	}

	/// <summary>
	/// Async initialization of the engine core.
	/// </summary>
	private async void InitializeEngineAsync()
	{
		try
		{
			// Create and initialize pure C# engine
			_engine = new Lumora.Core.Engine
			{
				AutoHostLocalHome = this.AutoHostLocalHome,
				AutoConnectLocalHome = this.AutoConnectLocalHome
			};

			AquaLogger.Log("EngineDriver: Starting engine initialization...");
			await _engine.InitializeAsync();

			AquaLogger.Log("EngineDriver: Initialization complete.");
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"EngineDriver: Engine initialization failed: {ex.Message}");
			AquaLogger.Error($"Stack trace: {ex.StackTrace}");
		}
	}

	private void InitializeInputRoot()
	{
		// Get InputRoot directly by name (more reliable than NodePath)
		_inputRoot = GetNodeOrNull<Node3D>("InputRoot");

		if (_inputRoot == null)
		{
			AquaLogger.Error("EngineDriver: InputRoot node not found! Input will not work.");
		}
		else
		{
			AquaLogger.Log($"EngineDriver: InputRoot found at {_inputRoot.GetPath()}");
		}
	}

	private void InitializeInput()
	{
		if (_inputRoot == null)
		{
			AquaLogger.Warn("EngineDriver: InputRoot is null, cannot initialize input");
			return;
		}

		_xrInterface = XRServer.FindInterface("OpenXR");

		if (IsInstanceValid(_xrInterface) && _xrInterface.IsInitialized())
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
			GetViewport().UseXR = true;

			// TODO: VR input needs redesign with new InputInterface system
			// var vrInput = VRInput.PackedScene.Instantiate<VRInput>();
			// _inputProvider = vrInput;
			// _inputRoot.AddChild(vrInput);

			// Set the global singleton so PlayerCharacterController can access it
			// IInputProvider.Instance = _inputProvider;
			AquaLogger.Log("EngineDriver: XR interface initialized (STUB - VR input needs InputInterface integration)");
		}
		else
		{
			// TODO: Use new InputInterface system instead of old drivers
			// Create Godot input drivers
			// var mouseDriver = new Aquamarine.Source.Godot.Input.Drivers.GodotMouseDriver();
			// var keyboardDriver = new Aquamarine.Source.Godot.Input.Drivers.GodotKeyboardDriver();

			// TODO: Integrate with InputInterface
			// Engine.InputInterface.RegisterMouseDriver(mouseDriver);
			// Engine.InputInterface.RegisterKeyboardDriver(keyboardDriver);

			AquaLogger.Log("EngineDriver: Desktop input initialization (STUB - needs InputInterface integration)");
		}
	}

	private void InitializeManagers()
	{
		// TODO: LocalDatabase deleted - needs platform-agnostic redesign
		// Initialize LocalDatabase
		// if (LocalDatabase.Instance == null)
		// {
		// 	var database = new LocalDatabase();
		// 	AddChild(database);
		// 	AquaLogger.Log("EngineDriver: LocalDatabase initialized.");
		// }

		// TODO: LoginManager and DiscordManager not yet implemented
		// Initialize LoginManager
		// if (LoginManager.Instance == null)
		// {
		//     var loginManager = new LoginManager();
		//     AddChild(loginManager);
		//     AquaLogger.Log("EngineDriver: LoginManager initialized.");
		// }

		// Initialize DiscordManager
		// if (DiscordManager.Instance == null)
		// {
		//     var discordManager = new DiscordManager();
		//     AddChild(discordManager);
		//     discordManager.InitializeDiscord();
		//     AquaLogger.Log("EngineDriver: DiscordManager initialized.");
		// }

		// DiscordManager.Instance?.UpdatePresence("Starting Game", "Main Menu", "lumoravralpha", "Lumora VR");
		AquaLogger.Log("EngineDriver: LoginManager and DiscordManager (STUBS - not implemented)");

		// TODO: Initialize UserSpaceManager (CLIENT-SIDE ONLY) - needs platform-agnostic redesign
		// _userSpaceManager = new UserSpaceManager();
		// AddChild(_userSpaceManager);
		// AquaLogger.Log("EngineDriver: UserSpaceManager created.");
	}

	public override void _Input(InputEvent @event)
	{
		base._Input(@event);

		if (@event.IsActionPressed("ToggleDebug"))
		{
			// TODO: Implement ShowDebug on Lumora.Core.Engine
			// _engine.ShowDebug = !_engine.ShowDebug;
			// AquaLogger.Log($"Debug mode: {_engine.ShowDebug}");
		}
	}

	public override void _Process(double delta)
	{
		// Delegate to pure C# engine
		_engine?.Update(delta);

		// TODO: UserSpace setup - needs platform-agnostic redesign
		// Check if there's a pending userspace setup
		// var pendingWorld = _engine?.GetPendingUserSpaceSetup();
		// if (pendingWorld?.LocalUser != null && _userSpaceManager != null)
		// {
		// 	AquaLogger.Log("EngineDriver: Setting up UserSpace for local user...");
		// 	UserSpaceManager.Instance?.SetupUserSpace(pendingWorld.LocalUser);
		// 	_engine?.ClearPendingUserSpaceSetup();
		// }
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			AquaLogger.Log("EngineDriver: Shutting down...");
			_engine?.Dispose();
		}

		base.Dispose(disposing);
	}
}
