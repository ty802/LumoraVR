// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Scripting;

// What a person wants of one batch the host holds for them: undone or not, and whether their history has
// let it go. A state rather than a message, so several undos in one frame still add up to the right one.
// Only its owner writes it; the host takes nothing written by anyone else. -xlinka
public sealed class ScriptUndoRequest : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<int> Id = new();
    public readonly Sync<bool> Undone = new();
    public readonly Sync<bool> LetGo = new();

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
        dictionary.Add("Id", Id.Save(control));
        dictionary.Add("Undone", Undone.Save(control));
        dictionary.Add("LetGo", LetGo.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Id") is { } idNode)
            Id.Load(idNode, control);
        if (dictionary.TryGetNode("Undone") is { } undoneNode)
            Undone.Load(undoneNode, control);
        if (dictionary.TryGetNode("LetGo") is { } letGoNode)
            LetGo.Load(letGoNode, control);
    }

    public override object? GetValueAsObject() => Id.Value;
}
