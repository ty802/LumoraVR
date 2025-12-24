using System;
using System.IO;
using Lumora.Core;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Network replicator for Slots.
/// When a FullBatch is received, this creates the Slot objects that don't exist yet.
/// This is the key to world synchronization - clients can now receive world structure.
/// </summary>
public class SlotReplicator : ReplicatedDictionary<RefID, Slot>
{
    public SlotReplicator()
    {
        // Hook into element added to initialize slots
        OnElementAdded += HandleSlotAdded;
    }

    /// <summary>
    /// Encode a RefID key to the stream.
    /// </summary>
    protected override void EncodeKey(BinaryWriter writer, RefID key)
    {
        writer.Write7BitEncoded((ulong)key);
    }

    /// <summary>
    /// Encode slot data to the stream.
    /// Slot data itself is empty here - the slot's sync members are encoded separately
    /// as their own SyncElements with their own RefIDs.
    /// </summary>
    protected override void EncodeElement(BinaryWriter writer, Slot element)
    {
        // Intentionally empty - slot structure is encoded in the key list,
        // slot VALUES (name, position, etc.) are synced as separate SyncElements
    }

    /// <summary>
    /// Decode a RefID key from the stream.
    /// </summary>
    protected override RefID DecodeKey(BinaryReader reader)
    {
        return new RefID(reader.Read7BitEncoded());
    }

    /// <summary>
    /// Decode (CREATE) a Slot from the stream.
    /// This is the magic - it creates a new Slot object during network decode!
    /// </summary>
    protected override Slot DecodeElement(BinaryReader reader)
    {
        // Create a new slot - it will be initialized in HandleSlotAdded
        return new Slot();
    }

    /// <summary>
    /// Initialize the replicator with a World.
    /// </summary>
    public void Initialize(World world, string name, IWorldElement? parent)
    {
        base.Initialize(world, parent);
    }

    /// <summary>
    /// Handle slot addition - initialize and register with World.
    /// </summary>
    private void HandleSlotAdded(ReplicatedDictionary<RefID, Slot> dict, RefID key, Slot slot, bool isNew)
    {
        if (World == null) return;

        // Only initialize if this slot doesn't already have a world
        if (slot.World == null)
        {
            // Initialize with the world using the network-assigned RefID
            slot.InitializeFromReplicator(World, key);

            // Register with the world's slot tracking
            World.RegisterSlot(slot);
        }
    }

    public override void Dispose()
    {
        OnElementAdded -= HandleSlotAdded;
        base.Dispose();
    }
}
