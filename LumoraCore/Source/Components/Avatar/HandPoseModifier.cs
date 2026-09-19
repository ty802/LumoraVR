// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Avatar;

// Per-finger curl and splay offsets for one hand, in degrees. Curl adds the same bend at every joint
// of the finger; splay turns the finger at the knuckle. Nested so a modifier carries one per side.
public sealed class HandPoseOffsets : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<float> ThumbCurl = new();
    public readonly Sync<float> IndexCurl = new();
    public readonly Sync<float> MiddleCurl = new();
    public readonly Sync<float> RingCurl = new();
    public readonly Sync<float> PinkyCurl = new();
    public readonly Sync<float> ThumbSplay = new();
    public readonly Sync<float> IndexSplay = new();
    public readonly Sync<float> MiddleSplay = new();
    public readonly Sync<float> RingSplay = new();
    public readonly Sync<float> PinkySplay = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    public void Apply(floatQ[] hierarchyRotations)
    {
        const float toRadians = MathF.PI / 180f;
        HandPoseHelper.ModifyPose(hierarchyRotations,
            ThumbCurl.Value * toRadians, IndexCurl.Value * toRadians, MiddleCurl.Value * toRadians,
            RingCurl.Value * toRadians, PinkyCurl.Value * toRadians,
            ThumbSplay.Value * toRadians, IndexSplay.Value * toRadians, MiddleSplay.Value * toRadians,
            RingSplay.Value * toRadians, PinkySplay.Value * toRadians);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("ThumbCurl", ThumbCurl.Save(control));
        dictionary.Add("IndexCurl", IndexCurl.Save(control));
        dictionary.Add("MiddleCurl", MiddleCurl.Save(control));
        dictionary.Add("RingCurl", RingCurl.Save(control));
        dictionary.Add("PinkyCurl", PinkyCurl.Save(control));
        dictionary.Add("ThumbSplay", ThumbSplay.Save(control));
        dictionary.Add("IndexSplay", IndexSplay.Save(control));
        dictionary.Add("MiddleSplay", MiddleSplay.Save(control));
        dictionary.Add("RingSplay", RingSplay.Save(control));
        dictionary.Add("PinkySplay", PinkySplay.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        LoadMember(dictionary, "ThumbCurl", ThumbCurl, control);
        LoadMember(dictionary, "IndexCurl", IndexCurl, control);
        LoadMember(dictionary, "MiddleCurl", MiddleCurl, control);
        LoadMember(dictionary, "RingCurl", RingCurl, control);
        LoadMember(dictionary, "PinkyCurl", PinkyCurl, control);
        LoadMember(dictionary, "ThumbSplay", ThumbSplay, control);
        LoadMember(dictionary, "IndexSplay", IndexSplay, control);
        LoadMember(dictionary, "MiddleSplay", MiddleSplay, control);
        LoadMember(dictionary, "RingSplay", RingSplay, control);
        LoadMember(dictionary, "PinkySplay", PinkySplay, control);
    }

    public override object? GetValueAsObject() => null;

    private static void LoadMember(DataTreeDictionary dictionary, string name, SyncElement member, LoadControl control)
    {
        if (dictionary.TryGetNode(name) is { } child)
            member.Load(child, control);
    }
}

// Adds curl and splay on top of an upstream pose, each side with its own offsets. Runs in hierarchy
// space, so the added curl folds every joint of a finger the way the presets are built. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandPoseModifier : HandPoseProcessor
{
    public readonly SyncRef<IHandPoseSourceComponent> Source = null!;

    public readonly HandPoseOffsets LeftOffsets = new();

    public readonly HandPoseOffsets RightOffsets = new();

    protected override void UpdatePoses()
    {
        var source = Source?.Target;
        foreach (var side in HandPoseHelper.Sides)
            Data(side).Tracking = false;
        SetTracksMetacarpals(false);
        if (source == null)
            return;

        SetTracksMetacarpals(source.TracksMetacarpals);
        foreach (var side in HandPoseHelper.Sides)
        {
            if (!source.IsHandTracked(side))
                continue;
            var data = Data(side);
            if (!HandPoseHelper.Read(source, side, data.Rotations, data.Positions, out data.HasPositions))
                continue;

            var positions = data.HasPositions ? data.Positions : null;
            HandPoseHelper.ToHierarchy(positions, data.Rotations);
            (side == Chirality.Left ? LeftOffsets : RightOffsets).Apply(data.Rotations);
            HandPoseHelper.ToFlat(positions, data.Rotations);
            data.Tracking = true;
        }
    }
}
