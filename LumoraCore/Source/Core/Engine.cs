using System;
using System.Threading.Tasks;
using Lumora.Core.Logging;
using Lumora.Core.Management;
using Lumora.Core.Helpers;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

/// <summary>
/// Core engine singleton managing all engine subsystems.
/// </summary>
public class Engine : IDisposable
{
	private static Engine _instance;

	private bool _initialized;
	private bool _hostingLocalHome;
	private int? _localHomePort;
	private World _pendingUserSpaceSetup;

	// Core subsystems
	public WorldManager WorldManager { get; private set; }
	public FocusManager FocusManager { get; private set; }
	public Input.InputInterface InputInterface { get; set; }

	public static Engine Instance
	{
		get
		{
			if (_instance == null)
			{
				throw new InvalidOperationException("Engine not initialized. Call Engine.InitializeAsync() first.");
			}
			return _instance;
		}
	}

	public static bool ShowDebug { get; set; } = true;
	public static bool IsDedicatedServer { get; set; } = false;

	/// <summary>
	/// Auto-host local home world on startup.
	/// </summary>
	public bool AutoHostLocalHome { get; set; } = true;

	/// <summary>
	/// Auto-connect to local home on startup.
	/// </summary>
	public bool AutoConnectLocalHome { get; set; } = true;

	/// <summary>
	/// Initialize the engine and all subsystems asynchronously.
	/// </summary>
	public async Task InitializeAsync()
	{
		if (_initialized)
		{
			AquaLogger.Warn("Engine already initialized, skipping.");
			return;
		}

		if (_instance != null && _instance != this)
		{
			throw new InvalidOperationException("Engine singleton already exists!");
		}

		_instance = this;
		AquaLogger.Log("Engine: Initializing core systems...");

		// Initialize FocusManager
		FocusManager = new FocusManager();
		AquaLogger.Log("Engine: FocusManager initialized.");

		// Initialize WorldManager asynchronously
		WorldManager = new WorldManager();
		await WorldManager.InitializeAsync(this);

		ProcessStartupArguments();

		_initialized = true;
		AquaLogger.Log("Engine: Core systems initialized successfully.");
	}

	private void ProcessStartupArguments()
	{
		bool skipLocalHome = false;
		IsDedicatedServer = false;

		if (!skipLocalHome && AutoHostLocalHome)
		{
			StartLocalHome();

			if (AutoConnectLocalHome)
			{
				SwitchToLocalHome();
			}
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

		var world = WorldManager?.StartSession("LocalHome", (ushort)_localHomePort.Value, GetHostUserName(), "LocalHome");
		if (world == null)
		{
			AquaLogger.Error("Engine: Failed to start LocalHome session.");
			return;
		}

		_hostingLocalHome = true;
		AquaLogger.Log($"Engine: LocalHome hosted on port {_localHomePort.Value}.");

		// Store reference for userspace setup (will be handled by EngineDriver)
		_pendingUserSpaceSetup = world;
	}

	/// <summary>
	/// Get the world pending userspace setup (for EngineDriver to handle).
	/// </summary>
	public World GetPendingUserSpaceSetup()
	{
		return _pendingUserSpaceSetup;
	}

	/// <summary>
	/// Clear the pending userspace setup.
	/// </summary>
	public void ClearPendingUserSpaceSetup()
	{
		_pendingUserSpaceSetup = null;
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

	/// <summary>
	/// Update the engine.
	/// </summary>
	public void Update(double delta)
	{
		if (!_initialized)
			return;

		WorldManager?.Update(delta);
	}

	/// <summary>
	/// Dispose the engine and all subsystems.
	/// </summary>
	public void Dispose()
	{
		AquaLogger.Log("Engine: Shutting down...");

		WorldManager?.Dispose();

		if (_instance == this)
		{
			_instance = null;
		}

		_initialized = false;
	}
}
