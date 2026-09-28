// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Scripting;

// One undo batch the host holds for a person, as their machine sees it: its number, its name, the author
// of the script that recorded it, when that was a person, and whether the host has let it go. Only the
// host writes any of it. -xlinka
public sealed class ScriptUndoOffer : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<int> Id = new();
    public readonly Sync<string> Description = new();
    public readonly SyncRef<User> Author = new();
    public readonly Sync<bool> Dropped = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        Id.MarkHostOnly();
        Description.MarkHostOnly();
        Author.MarkHostOnly();
        Dropped.MarkHostOnly();
    }

    // Recorded by a person's script, and that person is no longer here.
    public bool AuthorGone => !Author.Value.IsNull && Author.Target is not { IsDestroyed: false };

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Id", Id.Save(control));
        dictionary.Add("Description", Description.Save(control));
        dictionary.Add("Author", Author.Save(control));
        dictionary.Add("Dropped", Dropped.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Id") is { } idNode)
            Id.Load(idNode, control);
        if (dictionary.TryGetNode("Description") is { } descriptionNode)
            Description.Load(descriptionNode, control);
        if (dictionary.TryGetNode("Author") is { } authorNode)
            Author.Load(authorNode, control);
        if (dictionary.TryGetNode("Dropped") is { } droppedNode)
            Dropped.Load(droppedNode, control);
    }

    public override object? GetValueAsObject() => Id.Value;
}
