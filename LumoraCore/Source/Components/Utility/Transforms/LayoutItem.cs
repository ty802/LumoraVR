// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// One laid-out child: the slot, and the drives that hold its transform.
//
// The drives live here rather than being made on the fly because a drive has to be a MEMBER to
// replicate and to survive a save - a layout rebuilt from loose runtime links would come back from a
// load with every child's position free again. -xlinka
public sealed class LayoutItem : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<Slot> Root = new();

    public readonly FieldDrive<float3> Position = new();

    public readonly FieldDrive<floatQ> Rotation = new();

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
        dictionary.Add("Root", Root.Save(control));
        dictionary.Add("Position", Position.Save(control));
        dictionary.Add("Rotation", Rotation.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Root") is { } rootNode)
            Root.Load(rootNode, control);
        if (dictionary.TryGetNode("Position") is { } positionNode)
            Position.Load(positionNode, control);
        if (dictionary.TryGetNode("Rotation") is { } rotationNode)
            Rotation.Load(rotationNode, control);
    }

    public override object? GetValueAsObject() => Root.Target;
}
