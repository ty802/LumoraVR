// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Helio.UI.Layout;

// One page of a PageCarousel: the anchors that place it and the switch that shows it. Members, so the drives
// replicate and come back from a save.
public sealed class CarouselPage : SyncElement, ISyncMemberCopy
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly FieldDrive<float2> AnchorMin = new();
    public readonly FieldDrive<float2> AnchorMax = new();
    public readonly FieldDrive<bool> Active = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        AnchorMin.LocalValueOnly = true;
        AnchorMax.LocalValueOnly = true;
        Active.LocalValueOnly = true;
    }

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not CarouselPage other || ReferenceEquals(other, this))
            return;
        copyChild(other.AnchorMin, AnchorMin);
        copyChild(other.AnchorMax, AnchorMax);
        copyChild(other.Active, Active);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("AnchorMin", AnchorMin.Save(control));
        dictionary.Add("AnchorMax", AnchorMax.Save(control));
        dictionary.Add("Active", Active.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("AnchorMin") is { } anchorMin)
            AnchorMin.Load(anchorMin, control);
        if (dictionary.TryGetNode("AnchorMax") is { } anchorMax)
            AnchorMax.Load(anchorMax, control);
        if (dictionary.TryGetNode("Active") is { } active)
            Active.Load(active, control);
    }

    public override object? GetValueAsObject() => AnchorMin.Target;
}
