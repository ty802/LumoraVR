// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Input;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Avatar;

// A body part a seat poses and the filters it poses it with. Required means a sitter with nothing on that
// part (no tracker, no socket) is given a stand-in for the filters to work on; without it the part is left
// to whatever already moves it.
public sealed class SeatBodyPose : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<BodyNode> Node = new(BodyNode.Hips);

    public readonly Sync<bool> Required = new(true);

    public readonly SyncList<PoseFilterEntry> Filters = new();

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
        dictionary.Add("Node", Node.Save(control));
        dictionary.Add("Required", Required.Save(control));
        dictionary.Add("Filters", Filters.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Node") is { } bodyNode)
            Node.Load(bodyNode, control);
        if (dictionary.TryGetNode("Required") is { } required)
            Required.Load(required, control);
        if (dictionary.TryGetNode("Filters") is { } filters)
            Filters.Load(filters, control);
    }

    public override object? GetValueAsObject() => Node.Value;
}
