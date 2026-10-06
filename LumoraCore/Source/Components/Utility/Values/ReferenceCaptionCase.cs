// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// One case of a ReferenceCaption: the element it stands for, and the words, colour and icon it shows then. Its
// fields replicate and save one by one like any others.
public sealed class ReferenceCaptionCase<T> : SyncElement, ISyncMemberCopy where T : class, IWorldElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<T> Match = new();
    public readonly Sync<string> Label = new(string.Empty);
    public readonly Sync<colorHDR> Tint = new(colorHDR.White);
    public readonly AssetRef<TextureAsset> Icon = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not ReferenceCaptionCase<T> other || ReferenceEquals(other, this))
            return;
        copyChild(other.Match, Match);
        copyChild(other.Label, Label);
        copyChild(other.Tint, Tint);
        copyChild(other.Icon, Icon);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Match", Match.Save(control));
        dictionary.Add("Label", Label.Save(control));
        dictionary.Add("Tint", Tint.Save(control));
        dictionary.Add("Icon", Icon.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Match") is { } match)
            Match.Load(match, control);
        if (dictionary.TryGetNode("Label") is { } label)
            Label.Load(label, control);
        if (dictionary.TryGetNode("Tint") is { } tint)
            Tint.Load(tint, control);
        if (dictionary.TryGetNode("Icon") is { } icon)
            Icon.Load(icon, control);
    }

    public override object? GetValueAsObject() => Label.Value;
}
