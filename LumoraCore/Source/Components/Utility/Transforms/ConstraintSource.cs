// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// One source of a TransformConstraint: the slot, how much it counts, and (for a parent constraint) where
// the constrained slot sits relative to it, in that slot's own space. -xlinka
public sealed class ConstraintSource : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<Slot> Source = new();

    public readonly Sync<float> Weight = new(1f);

    public readonly Sync<float3> PositionOffset = new(float3.Zero);

    public readonly Sync<floatQ> RotationOffset = new(floatQ.Identity);

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Source", Source.Save(control));
        dictionary.Add("Weight", Weight.Save(control));
        dictionary.Add("PositionOffset", PositionOffset.Save(control));
        dictionary.Add("RotationOffset", RotationOffset.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Source") is { } source)
            Source.Load(source, control);
        if (dictionary.TryGetNode("Weight") is { } weight)
            Weight.Load(weight, control);
        if (dictionary.TryGetNode("PositionOffset") is { } position)
            PositionOffset.Load(position, control);
        if (dictionary.TryGetNode("RotationOffset") is { } rotation)
            RotationOffset.Load(rotation, control);
    }

    public override object? GetValueAsObject() => Source.Target;
}
