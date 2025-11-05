using Godot;
using System.Collections.Generic;
using System.Linq;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// Manages the root slot for all users in a world.
/// each world has a UserRoot that contains all active users.
/// Users can be added/removed, and their slots are parented under this component's slot.
/// </summary>
public partial class UserRootComponent : Component
{
    private readonly Dictionary<string, Slot> _userSlots = new();

    /// <summary>
    /// Maximum number of users allowed in this world (0 = unlimited).
    /// </summary>
    public Sync<int> MaxUsers { get; private set; }

    public UserRootComponent()
    {
        MaxUsers = new Sync<int>(this, 0);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        AquaLogger.Log($"UserRootComponent initialized on {Slot?.SlotName.Value ?? "unknown slot"}");
    }

    /// <summary>
    /// Add a user to the world.
    /// Creates a slot for the user under this UserRoot.
    /// </summary>
    public Slot AddUser(string userId, string userName)
    {
        if (Slot == null)
        {
            AquaLogger.Error("UserRootComponent: Cannot add user - Slot is null");
            return null;
        }

        // Check if user already exists
        if (_userSlots.ContainsKey(userId))
        {
            AquaLogger.Warn($"UserRootComponent: User {userId} already exists");
            return _userSlots[userId];
        }

        // Check max users limit
        if (MaxUsers.Value > 0 && _userSlots.Count >= MaxUsers.Value)
        {
            AquaLogger.Warn($"UserRootComponent: Cannot add user {userId} - max users ({MaxUsers.Value}) reached");
            return null;
        }

        // Create user slot
        var userSlot = Slot.AddSlot($"User_{userName}");
        userSlot.Tag.Value = "User";
        _userSlots[userId] = userSlot;

        AquaLogger.Log($"UserRootComponent: Added user {userName} (ID: {userId})");
        return userSlot;
    }

    /// <summary>
    /// Remove a user from the world.
    /// </summary>
    public void RemoveUser(string userId)
    {
        if (!_userSlots.TryGetValue(userId, out var userSlot))
        {
            AquaLogger.Warn($"UserRootComponent: Cannot remove user {userId} - not found");
            return;
        }

        userSlot.Destroy();
        _userSlots.Remove(userId);

        AquaLogger.Log($"UserRootComponent: Removed user {userId}");
    }

    /// <summary>
    /// Get a user's slot by ID.
    /// </summary>
    public Slot GetUserSlot(string userId)
    {
        return _userSlots.TryGetValue(userId, out var slot) ? slot : null;
    }

    /// <summary>
    /// Get all user slots.
    /// </summary>
    public IEnumerable<Slot> GetAllUserSlots()
    {
        return _userSlots.Values.ToList();
    }

    /// <summary>
    /// Get the number of users currently in the world.
    /// </summary>
    public int GetUserCount()
    {
        return _userSlots.Count;
    }

    public override void OnUpdate(float delta)
    {
        // UserRoot is passive - no update logic needed
    }

    public override void OnDestroy()
    {
        // Clean up all user slots
        foreach (var userSlot in _userSlots.Values.ToList())
        {
            userSlot.Destroy();
        }
        _userSlots.Clear();

        base.OnDestroy();
    }
}
