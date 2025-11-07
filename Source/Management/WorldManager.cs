using System;
using System.IO;
using System.Collections.Generic;
using Godot;
using Aquamarine.Source.Core;
using Aquamarine.Source.Core.WorldTemplates;
using Aquamarine.Source.Logging;
using AquaLogger = Aquamarine.Source.Logging.Logger;
using AquaEngine = Aquamarine.Source.Core.Engine;

namespace Aquamarine.Source.Management
{
	/// <summary>
	/// Manages all worlds (local, hosted sessions, joined sessions) without special-casing.
	/// Replaces the old ServerManager/ClientManager split architecture.
	/// </summary>
	public partial class WorldManager : Node, IDisposable
	{
		public static WorldManager Instance;

		private Control _loadingScreen;
		private ProgressBar _progressBar;
		private WorldsManager _worldsManager;
		private bool _initialized = false;

		private readonly List<World> _worlds = new List<World>();
		private readonly object _worldsLock = new object();

		private AquaEngine _engine;

		[Signal]
		public delegate void WorldAddedEventHandler(World world);

		/// <summary>
		/// Currently active world instance (null if no world is active).
		/// </summary>
		public WorldInstance ActiveWorldInstance => _worldsManager?.ActiveWorld;

		/// <summary>
		/// Convenience access to the active world node.
		/// </summary>
		public World ActiveWorld => ActiveWorldInstance?.World;

		/// <summary>
		/// Get all managed worlds.
		/// </summary>
		public IReadOnlyList<World> Worlds
		{
			get
			{
				lock (_worldsLock)
				{
					return _worlds.AsReadOnly();
				}
			}
		}

		public override void _Ready()
		{
			Instance = this;
			// Don't auto-initialize - let Engine call Initialize()
		}

		/// <summary>
		/// Initialize the WorldManager. Called by Engine.
		/// </summary>
		public void Initialize(AquaEngine engine)
		{
			if (_initialized)
			{
				AquaLogger.Warn("WorldManager already initialized.");
				return;
			}

			_engine = engine;

			try
			{
				_worldsManager = FindWorldsManager();
				if (_worldsManager == null)
				{
					AquaLogger.Error("WorldManager: WorldsManager not found in scene tree. World loading will fail.");
				}

				_loadingScreen = GetNodeOrNull<Control>("/root/Root/HUDManager/LoadingMenu");
				if (_loadingScreen != null)
				{
					_loadingScreen.Visible = false;
					_progressBar = _loadingScreen.GetNodeOrNull<ProgressBar>("ProgressBar");
					if (_progressBar != null)
					{
						_progressBar.Value = 0;
					}
				}

				_initialized = true;
				AquaLogger.Log("WorldManager initialized.");
			}
			catch (Exception ex)
			{
				AquaLogger.Error($"WorldManager.Initialize failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Update all worlds. Called by Engine.
		/// </summary>
		public void Update(double delta)
		{
			// Update logic for worlds if needed
		}

		/// <summary>
		/// </summary>
		public World StartLocal(string worldName, string templateName = "Grid")
		{
			try
			{
				AquaLogger.Log($"WorldManager: Starting local world '{worldName}' with template '{templateName}'");

				// Create the world instance
				var worldInstance = EnsureWorld(worldName, templateName);
				if (worldInstance == null)
				{
					throw new InvalidOperationException($"Failed to create world '{worldName}'");
				}

				worldInstance.Privacy = WorldInstance.WorldPrivacyLevel.Hidden;
				var world = worldInstance.World;

				// Add to managed worlds
				lock (_worldsLock)
				{
					if (!_worlds.Contains(world))
					{
						_worlds.Add(world);
					}
				}

				// Switch to the world
				_worldsManager.SwitchToWorld(worldInstance.WorldId);

				// Configure world
				ConfigureWorld(world);

				// Emit event
				EmitSignal(SignalName.WorldAdded, world);

				AquaLogger.Log($"WorldManager: Local world '{worldName}' started successfully");
				return world;
			}
			catch (Exception ex)
			{
				AquaLogger.Error($"WorldManager: Failed to start local world '{worldName}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// </summary>
		public World StartSession(string worldName, string templateName = "Grid", ushort port = 7000, string hostUserName = null)
		{
			try
			{
				AquaLogger.Log($"WorldManager: Starting session '{worldName}' on port {port}");

				// Create the world instance
				var worldInstance = EnsureWorld(worldName, templateName);
				if (worldInstance == null)
				{
					throw new InvalidOperationException($"Failed to create world '{worldName}'");
				}

				worldInstance.Privacy = WorldInstance.WorldPrivacyLevel.Public;
				var world = worldInstance.World;

				// Start networking session
				world.StartSession(port, hostUserName);

				// Add to managed worlds
				lock (_worldsLock)
				{
					if (!_worlds.Contains(world))
					{
						_worlds.Add(world);
					}
				}

				// Switch to the world
				_worldsManager.SwitchToWorld(worldInstance.WorldId);

				// Configure world
				ConfigureWorld(world);

				// Emit event
				EmitSignal(SignalName.WorldAdded, world);

				AquaLogger.Log($"WorldManager: Session '{worldName}' started successfully on port {port}");
				return world;
			}
			catch (Exception ex)
			{
				AquaLogger.Error($"WorldManager: Failed to start session '{worldName}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// </summary>
		public World JoinSession(string worldName, string address, ushort port)
		{
			try
			{
				AquaLogger.Log($"WorldManager: Joining session at {address}:{port}");

				// Create a temporary world instance for the joined session
				var worldInstance = EnsureWorld(worldName, "Grid");
				if (worldInstance == null)
				{
					throw new InvalidOperationException($"Failed to create world for joining session");
				}

				worldInstance.Privacy = WorldInstance.WorldPrivacyLevel.Public;
				var world = worldInstance.World;

				// Join the session
				var uriBuilder = new UriBuilder("lnl", address, port);
				world.JoinSession(uriBuilder.Uri);

				// Add to managed worlds
				lock (_worldsLock)
				{
					if (!_worlds.Contains(world))
					{
						_worlds.Add(world);
					}
				}

				// Switch to the world
				_worldsManager.SwitchToWorld(worldInstance.WorldId);

				// Configure world
				ConfigureWorld(world);

				// Emit event
				EmitSignal(SignalName.WorldAdded, world);

				AquaLogger.Log($"WorldManager: Successfully joined session at {address}:{port}");
				return world;
			}
			catch (Exception ex)
			{
				AquaLogger.Error($"WorldManager: Failed to join session at {address}:{port}: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Get a world by name.
		/// </summary>
		public World GetWorldByName(string name)
		{
			lock (_worldsLock)
			{
				return _worlds.Find(w => w.WorldName.Value == name);
			}
		}

		/// <summary>
		/// Switch active world to the provided world instance.
		/// </summary>
		public void SwitchToWorld(World world)
		{
			if (world == null || _worldsManager == null)
				return;

			var instance = _worldsManager.GetWorldByName(world.WorldName.Value);
			if (instance != null)
			{
				_worldsManager.SwitchToWorld(instance.WorldId);
			}
		}

		/// <summary>
		/// Remove a world from management.
		/// </summary>
		public void RemoveWorld(World world)
		{
			lock (_worldsLock)
			{
				_worlds.Remove(world);
			}
		}

		// ===== LEGACY COMPATIBILITY METHODS =====

		/// <summary>
		/// Legacy method for loading worlds. Maps to appropriate StartLocal/StartSession.
		/// </summary>
		[Obsolete("Use StartLocal() or StartSession() instead")]
		public void LoadWorld(string worldPath, bool disconnectFromCurrent = true)
		{
			try
			{
				var descriptor = ParseWorldDescriptor(worldPath);

				// For now, treat all as local worlds
				StartLocal(descriptor.Name, descriptor.Template);
			}
			catch (Exception ex)
			{
				AquaLogger.Error($"WorldManager: Error loading world '{worldPath}': {ex.Message}");
			}
		}

		// ===== PRIVATE HELPER METHODS =====

		private WorldInstance EnsureWorld(string worldName, string templateName)
		{
			// Check if world already exists
			var existing = _worldsManager.GetWorldByName(worldName);
			if (existing != null)
			{
				AquaLogger.Log($"WorldManager: World '{worldName}' already exists, reusing");
				return existing;
			}

			// Validate template
			if (TemplateManager.GetTemplate(templateName) == null)
			{
				AquaLogger.Warn($"WorldManager: Template '{templateName}' not registered. Defaulting to Grid.");
				templateName = "Grid";
			}

			// Create new world
			var created = _worldsManager.CreateWorld(worldName, templateName);
			AquaLogger.Log($"WorldManager: Created new world '{worldName}' with template '{templateName}'");
			return created;
		}

		private void ConfigureWorld(World world)
		{
			if (world == null)
				return;

			// Ensure UserRoot and spawn points exist
			var userRoot = world.GetOrCreateUserRoot();
			world.GetSpawnPoints();

			AquaLogger.Log($"WorldManager: Configured world '{world.WorldName.Value}'");
		}


		private struct WorldDescriptor
		{
			public string Name;
			public string Template;
		}

		private WorldDescriptor ParseWorldDescriptor(string worldPath)
		{
			if (string.IsNullOrEmpty(worldPath))
			{
				return new WorldDescriptor { Name = "UntitledWorld", Template = "Grid" };
			}

			var fileName = Path.GetFileNameWithoutExtension(worldPath);
			if (string.IsNullOrEmpty(fileName))
			{
				fileName = "UntitledWorld";
			}

			string template = "Grid";
			if (fileName.Contains("Social", StringComparison.OrdinalIgnoreCase))
			{
				template = "Social Space";
			}
			else if (fileName.Contains("Empty", StringComparison.OrdinalIgnoreCase))
			{
				template = "Empty";
			}

			return new WorldDescriptor { Name = fileName, Template = template };
		}

		private WorldsManager FindWorldsManager()
		{
			return FindNodeRecursive<WorldsManager>(GetTree().Root);
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

		public new void Dispose()
		{
			AquaLogger.Log("WorldManager: Disposing...");

			lock (_worldsLock)
			{
				foreach (var world in _worlds)
				{
					try
					{
						world?.LeaveSession();
					}
					catch (Exception ex)
					{
						AquaLogger.Error($"WorldManager: Error disposing world: {ex.Message}");
					}
				}
				_worlds.Clear();
			}

			_initialized = false;
		}
	}
}
