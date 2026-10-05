// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets;

// One cutting plane of a slice material: a point on it and the normal of the side that is kept. Its fields
// replicate and save one by one; a change to either tells the material, which has to push the plane again.
public sealed class SlicePlane : SyncElement, ISyncMemberCopy
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<float3> Position = new();

    public readonly Sync<float3> Normal = new();

    internal PBS_Slice? Owner;

    // (normal, -dot(normal, point)): a point's signed distance is then one dot product plus w.
    public float4 Plane
    {
        get
        {
            var normal = Normal.Value;
            return new float4(normal.x, normal.y, normal.z, -float3.Dot(normal, Position.Value));
        }
    }

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        Position.Changed += OnFieldChanged;
        Normal.Changed += OnFieldChanged;
    }

    private void OnFieldChanged(IChangeable member) => Owner?.SlicersChanged();

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not SlicePlane other || ReferenceEquals(other, this))
            return;
        copyChild(other.Position, Position);
        copyChild(other.Normal, Normal);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Position", Position.Save(control));
        dictionary.Add("Normal", Normal.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Position") is { } positionNode)
            Position.Load(positionNode, control);
        if (dictionary.TryGetNode("Normal") is { } normalNode)
            Normal.Load(normalNode, control);
    }

    public override object? GetValueAsObject() => Normal.Value;
}
