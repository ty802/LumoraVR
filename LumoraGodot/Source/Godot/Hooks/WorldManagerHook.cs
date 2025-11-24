using Godot;
using Lumora.Core.Management;

namespace Aquamarine.Godot.Hooks;

/// <summary>
/// WorldManager hook for Godot - creates root node for all worlds.
/// Platform world manager hook for Godot.
/// </summary>
public class WorldManagerHook : IWorldManagerHook
{
	public WorldManager Owner { get; private set; }

	public Node3D Root { get; private set; }

	public static WorldManagerHook Constructor()
	{
		return new WorldManagerHook();
	}

	public void Initialize(WorldManager owner, object sceneRoot)
	{
		Owner = owner;

		// Create root node for all worlds
		Root = new Node3D();
		Root.Name = "WorldManager";

		// Add to scene root
		if (sceneRoot is Node node)
		{
			node.AddChild(Root);
			GD.Print($"WorldManagerHook: Added Root to scene tree under '{node.Name}'");
		}

		// Reset transform
		Root.Position = Vector3.Zero;
		Root.Rotation = Vector3.Zero;
		Root.Scale = Vector3.One;

		// Subscribe to world events to create WorldHooks
		Owner.WorldAdded += OnWorldAdded;
		Owner.WorldRemoved += OnWorldRemoved;
		GD.Print($"WorldManagerHook: Subscribed to world events");

		// Initialize hooks for existing worlds
		GD.Print($"WorldManagerHook: Initializing hooks for {Owner.Worlds.Count} existing worlds");
		foreach (var world in Owner.Worlds)
		{
			GD.Print($"WorldManagerHook: Creating hook for existing world '{world.WorldName.Value}'");
			OnWorldAdded(world);
		}
	}

	private void OnWorldAdded(Lumora.Core.World world)
	{
		GD.Print($"WorldManagerHook: OnWorldAdded called for world '{world.WorldName.Value}'");
		
		// Create WorldHook for the new world IMMEDIATELY
		// This must happen before any slots are created
		var worldHook = WorldHook.Constructor();
		world.Hook = worldHook;  // Set hook FIRST
		worldHook.Initialize(world);   // Then initialize it
		
		GD.Print($"WorldManagerHook: Created hook for world '{world.WorldName.Value}'");
		GD.Print($"WorldManagerHook: World.GodotSceneRoot is set: {world.GodotSceneRoot != null}");
	}

	private void OnWorldRemoved(Lumora.Core.World world)
	{
		// Destroy WorldHook
		if (world.Hook is WorldHook worldHook)
		{
			worldHook.Destroy();
			world.Hook = null;
		}

		GD.Print($"WorldManagerHook: Removed hook for world '{world.WorldName.Value}'");
	}

	public void Destroy()
	{
		if (Root != null && GodotObject.IsInstanceValid(Root))
		{
			Root.QueueFree();
		}

		Root = null;
		Owner = null;
	}
}
