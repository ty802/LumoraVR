// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// One channel of a clip wired to one field.
//
// The track is named by INDEX rather than by position in the list. Binding positionally would mean a
// clip that gains, loses or reorders a channel silently re-points every binding after it - a foot
// track driving a jaw. An explicit index also lets unbindable channels simply have no entry, so
// "bound" and "unbound" are countable instead of guessed. -xlinka
public sealed class AnimationBinding : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<int> TrackIndex = new();

    // replicates and persists like any other link
    public readonly AnyFieldDrive Target = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        // Members sync as their own SyncElements; the container carries no payload.
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
    }

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
    }

    protected override void InternalClearDirty()
    {
    }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("TrackIndex", TrackIndex.Save(control));
        dictionary.Add("Target", Target.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("TrackIndex") is { } trackIndexNode)
            TrackIndex.Load(trackIndexNode, control);
        if (dictionary.TryGetNode("Target") is { } targetNode)
            Target.Load(targetNode, control);
    }

    public override object? GetValueAsObject() => TrackIndex.Value;
}
