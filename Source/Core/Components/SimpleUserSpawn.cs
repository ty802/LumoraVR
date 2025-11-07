using System.Linq;
using Godot;
using Aquamarine.Source.Scene.RootObjects;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// Simple user spawner component that automatically spawns player character controllers
/// when users join the world.
/// </summary>
public class SimpleUserSpawn : Component, IWorldEventReceiver
{
	private readonly System.Collections.Generic.Dictionary<User, Slot> _userSlots = new();

	public Sync<PackedScene> PlayerScene { get; private set; }

	public SimpleUserSpawn()
	{
		PlayerScene = new Sync<PackedScene>(null);
	}

	public override void OnStart()
	{
		base.OnStart();
		
		// Register as world event receiver
		World.RegisterEventReceiver(this);
	}

	public override void OnDestroy()
	{
		// Unregister from world events
		World.UnregisterEventReceiver(this);
		
		base.OnDestroy();
	}

	public bool HasEventHandler(World.WorldEvent eventType)
	{
		return eventType == World.WorldEvent.OnUserJoined || eventType == World.WorldEvent.OnUserLeft;
	}

	public void OnUserJoined(User user)
	{
		AquaLogger.Log($"SimpleUserSpawn: Spawning user {user.ReferenceID} ({user.UserName.Value})");

		// Find Users slot by name (more reliable than tag)
		var usersSlot = World.RootSlot.FindChild("Users", false);
		
		// If not found by name, try by tag
		if (usersSlot == null)
		{
			usersSlot = World.FindSlotsByTag("UserRoot").FirstOrDefault();
		}
		
		// If still not found, create it
		if (usersSlot == null)
		{
			AquaLogger.Warn("SimpleUserSpawn: Users slot not found, creating it now");
			usersSlot = World.RootSlot.AddSlot("Users");
			usersSlot.Tag.Value = "UserRoot";
		}

		// Create user slot UNDER Users
		Slot userSlot = usersSlot.AddSlot($"User {user.ReferenceID}");
		userSlot.Persistent.Value = false; // Users are not persistent

		// Attach UserRoot component
		var userRoot = userSlot.AttachComponent<UserRootComponent>();
		userRoot.TargetUser = user;

		// Store reference
		_userSlots[user] = userSlot;

		// Spawn player character
		SpawnPlayerCharacter(userRoot);
	}

	public void OnUserLeft(User user)
	{
		AquaLogger.Log($"SimpleUserSpawn: Despawning user {user.ReferenceID} ({user.UserName.Value})");

		if (_userSlots.TryGetValue(user, out var userSlot))
		{
			// Destroy user's slot (and all children)
			userSlot.Destroy();
			_userSlots.Remove(user);
		}
	}

	private void SpawnPlayerCharacter(UserRootComponent userRoot)
	{
		// Determine spawn position
		Vector3 spawnPosition = DetermineSpawnPosition();

		// Use configured player scene or default
		PackedScene sceneToUse = PlayerScene.Value ?? PlayerCharacterController.PackedScene;

		if (sceneToUse == null)
		{
			AquaLogger.Error("SimpleUserSpawn: No player scene configured!");
			return;
		}

		// Instantiate player
		var playerInstance = sceneToUse.Instantiate();
		if (playerInstance is not PlayerCharacterController player)
		{
			AquaLogger.Error($"SimpleUserSpawn: Player scene did not instantiate a PlayerCharacterController!");
			playerInstance.QueueFree();
			return;
		}

		// Setup player
		player.Name = $"PlayerController_{userRoot.TargetUser.ReferenceID}";
		player.SetPlayerAuthority((int)userRoot.TargetUser.ReferenceID);

		// Add to user's slot (Slot IS a Node3D, so we can add directly)
		userRoot.Slot.AddChild(player);
		player.GlobalPosition = spawnPosition;

		AquaLogger.Log($"SimpleUserSpawn: Spawned player for user {userRoot.TargetUser.ReferenceID} at {spawnPosition}");
	}

	private Vector3 DetermineSpawnPosition()
	{
		// Look for spawn points in the world
		var spawnPoints = World.GetSpawnPoints();
		
		foreach (var spawnComponent in spawnPoints)
		{
			if (spawnComponent.CanSpawn())
			{
				// Slot IS a Node3D, so GlobalPosition is directly available
				return spawnComponent.Slot.GlobalPosition;
			}
		}

		// Default spawn position
		return new Vector3(0, 1.7f, 0);
	}

	public void OnFocusChanged(World.WorldFocus focus)
	{
		// Not used
	}

	public void OnWorldDestroy()
	{
		// Cleanup all user slots
		foreach (var kvp in _userSlots)
		{
			kvp.Value.Destroy();
		}
		_userSlots.Clear();
	}

}
