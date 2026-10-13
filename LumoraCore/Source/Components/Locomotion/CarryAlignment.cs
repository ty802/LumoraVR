// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components;

// One way up a UserCarrier takes riders in: a direction in a space, and how far a body's down may lean from it.
// An empty space is the world's. A wall walk or an upside-down stage lists the downs it carries; anything
// leaning further from all of them steps on and is not taken.
public sealed class CarryAlignment : SyncElement, ISyncMemberCopy
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<Slot> Space = new();

    public readonly Sync<float3> Direction = new(float3.Down);

    // Degrees.
    [Range(0f, 180f, "0")]
    public readonly Sync<float> MaxAngle = new(45f);

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    public bool Accepts(in float3 down)
    {
        var space = Space.Target;
        var direction = space is { IsDestroyed: false } ? space.LocalDirectionToGlobal(Direction.Value) : Direction.Value;
        return float3.Angle(direction, down) * (180f / MathF.PI) < MaxAngle.Value;
    }

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not CarryAlignment other || ReferenceEquals(other, this))
            return;
        copyChild(other.Space, Space);
        copyChild(other.Direction, Direction);
        copyChild(other.MaxAngle, MaxAngle);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Space", Space.Save(control));
        dictionary.Add("Direction", Direction.Save(control));
        dictionary.Add("MaxAngle", MaxAngle.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Space") is { } spaceNode)
            Space.Load(spaceNode, control);
        if (dictionary.TryGetNode("Direction") is { } directionNode)
            Direction.Load(directionNode, control);
        if (dictionary.TryGetNode("MaxAngle") is { } angleNode)
            MaxAngle.Load(angleNode, control);
    }

    public override object? GetValueAsObject() => Direction.Value;
}
