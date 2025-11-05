using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// Marks a location as a spawn point for players entering the world.
/// 
/// </summary>
public partial class SpawnPointComponent : Component
{
    /// <summary>
    /// Priority order for spawn selection (higher = more likely to be chosen).
    /// </summary>
    public Sync<int> Priority { get; private set; }

    /// <summary>
    /// Whether this spawn point is currently active.
    /// </summary>
    public Sync<bool> IsActive { get; private set; }

    /// <summary>
    /// Optional tag for categorizing spawn points (e.g., "Entry", "Respawn", "Event").
    /// </summary>
    public Sync<string> SpawnTag { get; private set; }

    public SpawnPointComponent()
    {
        Priority = new Sync<int>(this, 0);
        IsActive = new Sync<bool>(this, true);
        SpawnTag = new Sync<string>(this, "Default");
    }

    public override void OnAwake()
    {
        base.OnAwake();
        AquaLogger.Log($"SpawnPointComponent attached to {Slot?.SlotName.Value ?? "unknown slot"}");
    }

    /// <summary>
    /// Get the world position of this spawn point.
    /// </summary>
    public Vector3 GetSpawnPosition()
    {
        return Slot?.GlobalTransform.Origin ?? Vector3.Zero;
    }

    /// <summary>
    /// Get the world rotation of this spawn point.
    /// </summary>
    public Quaternion GetSpawnRotation()
    {
        return Slot?.GlobalTransform.Basis.GetRotationQuaternion() ?? Quaternion.Identity;
    }

    /// <summary>
    /// Check if a player can spawn at this point.
    /// </summary>
    public bool CanSpawn()
    {
        return IsActive.Value && Slot != null;
    }

    public override void OnUpdate(float delta)
    {
        // Spawn points are passive - no update logic needed
    }
}
