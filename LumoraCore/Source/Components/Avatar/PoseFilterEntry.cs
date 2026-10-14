// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Avatar;

// One filter a seat lays on a body pose and where it runs: higher priorities run first.
public sealed class PoseFilterEntry : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<IPoseFilter> Filter = new();

    public readonly Sync<int> Priority = new(0);

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
        dictionary.Add("Filter", Filter.Save(control));
        dictionary.Add("Priority", Priority.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Filter") is { } filter)
            Filter.Load(filter, control);
        if (dictionary.TryGetNode("Priority") is { } priority)
            Priority.Load(priority, control);
    }

    public override object? GetValueAsObject() => Filter.Target;
}
