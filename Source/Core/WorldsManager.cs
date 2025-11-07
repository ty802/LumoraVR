using System.Collections.Generic;
using System.Linq;
using Godot;
using Aquamarine.Source.Core.UserSpace;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core;

/// <summary>
/// Manages multiple world instances and handles world switching.
/// Hierarchy: Lumora -> WorldsManager -> Worlds (WorldInstance1, WorldInstance2, etc.)
/// 
/// </summary>
public partial class WorldsManager : Node
{
    public static WorldsManager Instance { get; private set; }

    private readonly Dictionary<string, WorldInstance> _worlds = new();
    private WorldInstance _activeWorld;
    private UserSpace.UserSpace _userSpace;

    [Signal]
    public delegate void WorldSwitchedEventHandler(string worldId);

    [Signal]
    public delegate void WorldCreatedEventHandler(string worldId);

    public WorldInstance ActiveWorld => _activeWorld;
    public IReadOnlyDictionary<string, WorldInstance> Worlds => _worlds;

    public override void _Ready()
    {
        base._Ready();

        if (Instance != null && Instance != this)
        {
            AquaLogger.Warn("WorldsManager instance already exists, removing duplicate");
            QueueFree();
            return;
        }

        Instance = this;
        Name = "Worlds"; // Set node name for hierarchy
        AquaLogger.Log("WorldsManager initialized");

        // Create UserSpace node
        _userSpace = new UserSpace.UserSpace();
        _userSpace.Name = "UserSpace";
        AddChild(_userSpace);
        AquaLogger.Log("UserSpace created");
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Create a new world instance.
    /// </summary>
    public WorldInstance CreateWorld(string worldName, string templateName = "Grid")
    {
        var worldInstance = new WorldInstance
        {
            WorldName = worldName,
            Name = worldName.Replace(" ", "_") // Clean name for node
        };

        AddChild(worldInstance);
        _worlds[worldInstance.WorldId] = worldInstance;

        // Apply template after adding to tree
        CallDeferred(nameof(ApplyTemplateDeferred), worldInstance.WorldId, templateName);

        AquaLogger.Log($"World '{worldName}' created with ID: {worldInstance.WorldId}");
        EmitSignal(SignalName.WorldCreated, worldInstance.WorldId);

        // If this is the first world, activate it
        if (_activeWorld == null)
        {
            CallDeferred(nameof(SwitchToWorldDeferred), worldInstance.WorldId);
        }
        else
        {
            // Deactivate new worlds by default
            worldInstance.Deactivate();
        }

        return worldInstance;
    }

    private void ApplyTemplateDeferred(string worldId, string templateName)
    {
        if (_worlds.TryGetValue(worldId, out var worldInstance))
        {
            worldInstance.ApplyTemplate(templateName);
        }
    }

    private void SwitchToWorldDeferred(string worldId)
    {
        SwitchToWorld(worldId);
    }

    /// <summary>
    /// Switch to a different world (disable current, enable new).
    /// </summary>
    public void SwitchToWorld(string worldId)
    {
        if (!_worlds.TryGetValue(worldId, out var targetWorld))
        {
            AquaLogger.Error($"World '{worldId}' not found");
            return;
        }

        // Deactivate current world
        if (_activeWorld != null && _activeWorld != targetWorld)
        {
            _activeWorld.Deactivate();
            AquaLogger.Log($"Deactivated world: {_activeWorld.WorldName}");
        }

        // Activate new world
        targetWorld.Activate();
        _activeWorld = targetWorld;

        AquaLogger.Log($"Switched to world: {targetWorld.WorldName}");
        EmitSignal(SignalName.WorldSwitched, worldId);

        // Update UserSpace to follow player in new world
        UpdateUserSpace();
    }

    /// <summary>
    /// Get a world instance by ID.
    /// </summary>
    public WorldInstance GetWorld(string worldId)
    {
        return _worlds.TryGetValue(worldId, out var world) ? world : null;
    }

    /// <summary>
    /// Get a world instance by name.
    /// </summary>
    public WorldInstance GetWorldByName(string worldName)
    {
        return _worlds.Values.FirstOrDefault(w => w.WorldName == worldName);
    }

    /// <summary>
    /// Delete a world instance.
    /// </summary>
    public void DeleteWorld(string worldId)
    {
        if (!_worlds.TryGetValue(worldId, out var worldInstance))
        {
            AquaLogger.Error($"Cannot delete world: '{worldId}' not found");
            return;
        }

        // If deleting active world, switch to another world first
        if (_activeWorld == worldInstance)
        {
            var otherWorld = _worlds.Values.FirstOrDefault(w => w != worldInstance);
            if (otherWorld != null)
            {
                SwitchToWorld(otherWorld.WorldId);
            }
            else
            {
                _activeWorld = null;
            }
        }

        _worlds.Remove(worldId);
        worldInstance.QueueFree();
        AquaLogger.Log($"World '{worldInstance.WorldName}' deleted");
    }

    /// <summary>
    /// Get all world instances.
    /// </summary>
    public List<WorldInstance> GetAllWorlds()
    {
        return _worlds.Values.ToList();
    }

	/// <summary>
	/// Update UserSpace for the active world.
	/// Players are spawned by the world's SimpleUserSpawn component.
	/// </summary>
	private void UpdateUserSpace()
	{
		if (_activeWorld == null || _activeWorld.World == null)
			return;

		// Ensure the world has user spawning infrastructure
		var userRoot = _activeWorld.World.GetOrCreateUserRoot();
		if (userRoot == null || userRoot.Slot == null)
		{
			AquaLogger.Error("Failed to get UserRoot for active world");
			return;
		}

		AquaLogger.Log($"WorldsManager: Active world '{_activeWorld.WorldName}' has UserRoot at {userRoot.Slot.GetPath()}");

		// UserSpace will update itself based on the active world
		// Player spawning is now handled by SimpleUserSpawn component in the world
	}

    /// <summary>
    /// Get the UserSpace instance.
    /// </summary>
    public UserSpace.UserSpace GetUserSpace()
    {
        return _userSpace;
    }
}
