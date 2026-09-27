// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Lumora.Core;

namespace Lumora.Core.Networking.Sync;

/// <summary>
/// Network collection for Slots.
/// When a FullBatch is received, this creates the Slot objects that don't exist yet.
/// </summary>
public class ReplicatedSlotCollection : ReplicatedObjectCollection<Slot>
{
    public ReplicatedSlotCollection()
    {
        // Hook into element added to initialize slots
        OnElementAdded += HandleSlotAdded;
        OnElementRemoved += HandleSlotRemoved;
    }

    // A removal that arrived by decode is another peer's destroy, and the slot goes here too. It used to
    // leave the registry and nothing else, so the object stayed alive and registered on every other peer.
    // A local removal is this peer's own destroy, already under way. On the world thread, where a slot's
    // hook may be torn down. -xlinka
    private void HandleSlotRemoved(ReplicatedDictionary<RefID, Slot> dict, RefID key, Slot slot)
    {
        if (!IsLoading || World == null || slot == null || slot.IsDestroyed)
            return;
        World.RunSynchronously(slot.DestroyFromReplication);
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
    /// Decode (CREATE) a Slot from the stream.
    /// This is the magic - it creates a new Slot object during network decode!
    /// </summary>
    protected override Slot DecodeElement(BinaryReader reader)
    {
        // Create a new slot - it will be initialized in HandleSlotAdded
        return new Slot();
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
            Lumora.Core.Logging.Logger.Debug($"SlotCollection.HandleSlotAdded: Creating slot RefID={key}, isNew={isNew}");

            // Initialize with the world using the network-assigned RefID
            slot.InitializeFromReplicator(World, key);

            // Keep sync members in loading state until their values
            // are decoded from FullBatch. This prevents them from being marked dirty
            // if the world transitions to Running before all values are decoded.
            foreach (var member in slot.SyncMembers)
            {
                if (member is SyncElement syncElement)
                {
                    World.SyncController.HoldLoading(syncElement);
                }
            }

            Lumora.Core.Logging.Logger.Debug($"SlotCollection.HandleSlotAdded: Slot initialized - Name='{slot.Name?.Value}', NameRefID={slot.Name?.ReferenceID}");

            // Register with the world's slot tracking
            World.RegisterSlot(slot);
        }
    }

    // SPAWN. Without the Spawn domain a user may still build under their own things, which is how every
    // user stands up their own hands and laser, but may not put a new slot anywhere else. The client asks
    // this before it spawns; the host asks it again here, of the sender, or a peer that skipped the
    // question lands its slot wherever it likes. A new slot's parent arrives as its ParentSlotRef record
    // in the same batch, at a fixed offset from the slot's id since every slot lays its members out in
    // the same order. A parent that is itself new in this batch is followed up to the first slot that
    // already exists. -xlinka
    public override MessageValidity Validate(BinaryMessageBatch syncMessage, BinaryReader reader, List<ValidationGroup.Rule> rules)
    {
        var validity = base.Validate(syncMessage, reader, rules);
        if (validity != MessageValidity.Valid || World?.IsAuthority != true)
            return validity;

        var sender = syncMessage.SenderUser;
        var permissions = World.DataModelPermissions;

        long position = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
        try
        {
            uint addCount = (uint)reader.Read7BitEncoded();
            var added = new HashSet<RefID>();
            for (int i = 0; i < addCount; i++)
            {
                _ = reader.ReadBoolean();
                added.Add(DecodeKey(reader));
                SkipElement(reader);
            }

            // A protected slot never leaves the registry from the wire: the root, user roots and anything
            // else marked protected refuses a local destroy, and a remote one would otherwise take the
            // registry entry and leave the slot behind.
            uint removeCount = (uint)reader.Read7BitEncoded();
            for (int i = 0; i < removeCount; i++)
            {
                var id = DecodeKey(reader);
                if (World.ReferenceController?.GetObjectOrNull(in id) is Slot { IsProtected: true })
                {
                    permissions?.ReportDenial(sender, DataModelDenialKind.Ownership, "removal of a protected slot");
                    return MessageValidity.Conflict;
                }
            }

            if (added.Count == 0 || permissions == null || permissions.AllowsDomain(sender, DataModelPermissionDomain.Spawn))
                return MessageValidity.Valid;

            foreach (var id in added)
            {
                if (!LandsUnderSendersOwn(id, added, syncMessage, sender))
                {
                    permissions.ReportDenial(sender, DataModelDenialKind.Ownership, "slot spawned outside the sender's own hierarchy without Spawn");
                    return MessageValidity.Conflict;
                }
            }
            return MessageValidity.Valid;
        }
        catch
        {
            return MessageValidity.Conflict;
        }
        finally
        {
            if (position >= 0)
                reader.BaseStream.Position = position;
        }
    }

    // Seeks the batch's shared reader; the caller puts it back.
    private bool LandsUnderSendersOwn(RefID slotId, HashSet<RefID> added, BinaryMessageBatch batch, User? sender)
    {
        var root = World?.RootSlot;
        if (root == null || sender == null)
            return false;

        long parentOffset = root.ReferenceID.DistanceTo(root.ParentSlotRef.ReferenceID);
        var current = slotId;
        for (int depth = 0; depth < 64; depth++)
        {
            int index = batch.FindDataRecordIndex(current.Offset(parentOffset));
            if (index < 0)
                return false;

            var parentId = SyncCoder.Decode<RefID>(batch.SeekDataRecord(index));
            if (added.Contains(parentId))
            {
                current = parentId;
                continue;
            }

            if (World!.ReferenceController?.GetObjectOrNull(in parentId) is not Slot parent)
                return false;

            if (parentId.GetUserByte() == sender.AllocationID.Value)
                return true;

            Lumora.Warden.IPermissionTarget gateParent = parent;
            return gateParent.IsUnderActorRoot(sender);
        }
        return false;
    }

    public override void Dispose()
    {
        OnElementAdded -= HandleSlotAdded;
        OnElementRemoved -= HandleSlotRemoved;
        base.Dispose();
    }
}
