using System;
using Godot;
using Aquamarine.Source.Logging;
using Aquamarine.Source.Management;
using Aquamarine.Source.Input;
using Aquamarine.Source.Helpers;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core;

/// <summary>
/// </summary>
public partial class Engine : Node
{
	private static Engine _instance;

	[Export] public NodePath InputRootPath { get; set; }
	[Export] public bool AutoHostLocalHome { get; set; } = true;
	[Export] public bool AutoConnectLocalHome { get; set; } = true;

	private Node3D _inputRoot;
	private XRInterface _xrInterface;
	private IInputProvider _inputProvider;

	private bool _initialized;
	private bool _hostingLocalHome;
	private int? _localHomePort;

	public static Engine Instance
	{
		get
		{
			if (_instance == null)
			{
				throw new InvalidOperationException("Engine not initialized. Ensure Engine node exists in the scene.");
			}
			return _instance;
		}
	}

	public static bool ShowDebug { get; private set; } = true;
	public static bool IsDedicatedServer
	{
		get
		{
			var args = ArgumentCache.Instance;
			if (args == null)
			{
				return false;
			}

			return args.IsFlagActive("run-server");
		}
	}

	public WorldManager WorldManager { get; private set; }

	public override void _Ready()
	{
		if (_instance != null && _instance != this)
		{
			AquaLogger.Error("Multiple Engine instances detected! This should never happen.");
			QueueFree();
			return;
		}

		_instance = this;

		// Get InputRoot directly by name (more reliable than NodePath)
		_inputRoot = GetNodeOrNull<Node3D>("InputRoot");
		
		if (_inputRoot == null)
		{
			AquaLogger.Error("Engine: InputRoot node not found! Input will not work.");
		}
		else
		{
			AquaLogger.Log($"Engine: InputRoot found at {_inputRoot.GetPath()}");
		}

		Initialize();
	}

	public override void _Input(InputEvent @event)
	{
		base._Input(@event);

		if (@event.IsActionPressed("ToggleDebug"))
		{
			SetShowDebug(!ShowDebug);
		}
	}

	public static void SetShowDebug(bool value)
	{
		ShowDebug = value;
	}

	private void Initialize()
	{
		if (_initialized)
		{
			AquaLogger.Warn("Engine already initialized, skipping.");
			return;
		}

		AquaLogger.Log("Engine: Initializing core systems...");

		WorldManager = GetNodeOrNull<WorldManager>("%WorldManager") ?? FindNodeRecursive<WorldManager>(GetTree().Root);
		if (WorldManager == null)
		{
			AquaLogger.Error("Engine: WorldManager not found!");
		}
		else
		{
			WorldManager.Initialize(this);
		}

		InitializeLocalDatabase();
		InitializeLoginManager();
		InitializeDiscordManager();
		InitializeInput();

		ProcessStartupArguments();

		_initialized = true;
		AquaLogger.Log("Engine: Core systems initialized successfully.");
	}

	private void ProcessStartupArguments()
	{
		var args = ArgumentCache.Instance;

		if (args?.Arguments.TryGetValue("port", out string portValue) ?? false)
		{
			if (int.TryParse(portValue, out var parsed))
			{
				_localHomePort = parsed;
			}
		}

		bool skipLocalHome = args?.IsFlagActive("nolocal") ?? false;

		if (!skipLocalHome && AutoHostLocalHome)
		{
			StartLocalHome();

			if (AutoConnectLocalHome || (args?.IsFlagActive("autoconnect") ?? false))
			{
				SwitchToLocalHome();
			}
		}
	}

	private void InitializeLocalDatabase()
	{
		if (LocalDatabase.Instance == null)
		{
			var database = new LocalDatabase();
			AddChild(database);
			AquaLogger.Log("Engine: LocalDatabase initialized.");
		}
	}

	private void InitializeLoginManager()
	{
		if (LoginManager.Instance == null)
		{
			var loginManager = new LoginManager();
			AddChild(loginManager);
			AquaLogger.Log("Engine: LoginManager initialized.");
		}
	}

	private void InitializeDiscordManager()
	{
		if (DiscordManager.Instance == null)
		{
			var discordManager = new DiscordManager();
			AddChild(discordManager);
			discordManager.InitializeDiscord();
			AquaLogger.Log("Engine: DiscordManager initialized.");
		}

		DiscordManager.Instance?.UpdatePresence("Starting Game", "Main Menu", "lumoravralpha", "Lumora VR");
	}

	private void InitializeInput()
	{
		if (_inputRoot == null)
		{
			AquaLogger.Warn("Engine: InputRoot is null, cannot initialize input");
			return;
		}

		_xrInterface = XRServer.FindInterface("OpenXR");

		if (IsInstanceValid(_xrInterface) && _xrInterface.IsInitialized())
		{
			DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
			GetViewport().UseXR = true;

			var vrInput = VRInput.PackedScene.Instantiate<VRInput>();
			_inputProvider = vrInput;
			_inputRoot.AddChild(vrInput);
			
			// Set the global singleton so PlayerCharacterController can access it
			IInputProvider.Instance = _inputProvider;
			AquaLogger.Log("Engine: XR interface initialized successfully.");
		}
		else
		{
			// Create input drivers
			var mouseDriver = new Aquamarine.Source.Input.Drivers.MouseDriver();
			var keyboardDriver = new Aquamarine.Source.Input.Drivers.KeyboardDriver();
			
			_inputRoot.AddChild(mouseDriver);
			_inputRoot.AddChild(keyboardDriver);
			
			var desktopInput = DesktopInput.PackedScene.Instantiate<DesktopInput>();
			_inputProvider = desktopInput;
			
			// Inject drivers into input provider
			desktopInput.MouseDriver = mouseDriver;
			desktopInput.KeyboardDriver = keyboardDriver;
			
			_inputRoot.AddChild(desktopInput);
			
			// Set the global singleton so PlayerCharacterController can access it
			IInputProvider.Instance = _inputProvider;
			AquaLogger.Log("Engine: Desktop input initialized with MouseDriver and KeyboardDriver");
		}
	}

	private void StartLocalHome()
	{
		if (_hostingLocalHome)
		{
			SwitchToLocalHome();
			return;
		}

		if (_localHomePort is null)
		{
			_localHomePort = SimpleIpHelpers.GetAvailablePortUdp(10) ?? 6000;
		}

		var world = WorldManager?.StartSession("LocalHome", "Grid", (ushort)_localHomePort.Value, GetHostUserName());
		if (world == null)
		{
			AquaLogger.Error("Engine: Failed to start LocalHome session.");
			return;
		}

		_hostingLocalHome = true;
		AquaLogger.Log($"Engine: LocalHome hosted on port {_localHomePort.Value}.");
	}

	private void SwitchToLocalHome()
	{
		var world = WorldManager?.GetWorldByName("LocalHome");
		if (world != null)
		{
			WorldManager.SwitchToWorld(world);
			AquaLogger.Log("Engine: Switched to LocalHome world.");
		}
		else
		{
			AquaLogger.Warn("Engine: LocalHome world not found when attempting to switch.");
		}
	}

	private string GetHostUserName()
	{
		// TODO: Integrate with account system when available
		return System.Environment.MachineName;
	}

	public void JoinLocalHome()
	{
		SwitchToLocalHome();
	}

	public void JoinServer(string address, int port, string worldName = "RemoteWorld")
	{
		WorldManager?.JoinSession(worldName, address, (ushort)port);
	}

	public void JoinNatServer(string identifier)
	{
		AquaLogger.Warn($"Engine: NAT join for session '{identifier}' not implemented yet.");
	}

	public void JoinNatServerRelay(string identifier)
	{
		AquaLogger.Warn($"Engine: Relay join for session '{identifier}' not implemented yet.");
	}

	public override void _Process(double delta)
	{
		if (!_initialized)
			return;

		WorldManager?.Update(delta);
	}

	private T FindNodeRecursive<T>(Node root) where T : class
	{
		if (root is T match)
		{
			return match;
		}

		foreach (Node child in root.GetChildren())
		{
			var result = FindNodeRecursive<T>(child);
			if (result != null)
			{
				return result;
			}
		}

		return null;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			AquaLogger.Log("Engine: Shutting down...");

			WorldManager?.Dispose();

			if (_instance == this)
			{
				_instance = null;
			}
		}

		base.Dispose(disposing);
	}
}
