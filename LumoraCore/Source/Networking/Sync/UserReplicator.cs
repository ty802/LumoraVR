using System;
using System.IO;
using Lumora.Core;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Network replicator for Users.
/// When a FullBatch is received, this creates User objects that don't exist yet.
/// </summary>
public class UserReplicator : ReplicatedDictionary<RefID, User>
{
    public UserReplicator()
    {
        // Hook into element added to register users with world
        OnElementAdded += HandleUserAdded;
        OnElementRemoved += HandleUserRemoved;
    }

    /// <summary>
    /// Encode a RefID key to the stream.
    /// </summary>
    protected override void EncodeKey(BinaryWriter writer, RefID key)
    {
        writer.Write7BitEncoded((ulong)key);
    }

    /// <summary>
    /// Encode user data to the stream.
    /// User's sync members are handled separately via the SyncElement system.
    /// </summary>
    protected override void EncodeElement(BinaryWriter writer, User element)
    {
        // Intentionally empty - user's sync members (UserName, AllocationID, etc.)
        // are encoded as separate SyncElements
    }

    /// <summary>
    /// Decode a RefID key from the stream.
    /// </summary>
    protected override RefID DecodeKey(BinaryReader reader)
    {
        return new RefID(reader.Read7BitEncoded());
    }

    /// <summary>
    /// Decode element - not used directly, we override CreateElementWithKey instead.
    /// </summary>
    protected override User DecodeElement(BinaryReader reader)
    {
        // This shouldn't be called - we override CreateElementWithKey
        throw new InvalidOperationException("UserReplicator requires CreateElementWithKey - DecodeElement should not be called");
    }

    /// <summary>
    /// Create a User with the given RefID key.
    /// Users need their RefID at construction time.
    /// </summary>
    protected override User CreateElementWithKey(RefID key, BinaryReader reader)
    {
        if (World == null)
            return null;

        // Create user with the network-assigned RefID (fromNetwork=true for allocation block)
        return new User(World, key, fromNetwork: true);
    }

    /// <summary>
    /// Initialize the replicator with a World.
    /// </summary>
    public void Initialize(World world, string name, IWorldElement? parent)
    {
        base.Initialize(world, parent);
    }

    /// <summary>
    /// Handle user addition - register with World.
    /// </summary>
    private void HandleUserAdded(ReplicatedDictionary<RefID, User> dict, RefID key, User user, bool isNew)
    {
        if (World == null || user == null) return;

        // Register user with world's user tracking
        World.RegisterUser(user);
    }

    /// <summary>
    /// Handle user removal - unregister from World.
    /// </summary>
    private void HandleUserRemoved(ReplicatedDictionary<RefID, User> dict, RefID key, User user)
    {
        if (World == null || user == null) return;

        // Unregister user from world
        World.UnregisterUser(user);
    }

    public override void Dispose()
    {
        OnElementAdded -= HandleUserAdded;
        OnElementRemoved -= HandleUserRemoved;
        base.Dispose();
    }
}
