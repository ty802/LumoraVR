// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets;

// One point a distance lerp material measures from, and the tint of the emission it adds. Its fields replicate
// and save one by one; a change to either tells the material, which has to push the point again.
public sealed class DistanceLerpPoint : SyncElement, ISyncMemberCopy
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<float3> Position = new();

    public readonly Sync<colorHDR> Tint = new(colorHDR.White);

    internal PBS_DistanceLerpMaterial? Owner;

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        Position.Changed += OnFieldChanged;
        Tint.Changed += OnFieldChanged;
    }

    private void OnFieldChanged(IChangeable member) => Owner?.PointsChanged();

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not DistanceLerpPoint other || ReferenceEquals(other, this))
            return;
        copyChild(other.Position, Position);
        copyChild(other.Tint, Tint);
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
        dictionary.Add("Tint", Tint.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Position") is { } positionNode)
            Position.Load(positionNode, control);
        if (dictionary.TryGetNode("Tint") is { } tintNode)
            Tint.Load(tintNode, control);
    }

    public override object? GetValueAsObject() => Position.Value;
}
