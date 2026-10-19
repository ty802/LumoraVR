// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Scripting;

// One constant or setting on an unpacked node. A plain value is kept as the JSON a packed graph would
// hold, so pack and unpack pass it through untouched. A bound world object is kept as a reference
// instead of a binding number, so duplicating or saving the graph remaps it like any other reference;
// a Write Value's "var" points at its Stored Value node the same way, and a Return's or a call's "fn" at its
// Function. -xlinka
public sealed class ScriptGraphValue : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<string> Port = new();
    public readonly Sync<string> Json = new();
    public readonly SyncRef<IWorldElement> Reference = new();

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
        dictionary.Add("Port", Port.Save(control));
        dictionary.Add("Json", Json.Save(control));
        dictionary.Add("Reference", Reference.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Port") is { } portNode)
            Port.Load(portNode, control);
        if (dictionary.TryGetNode("Json") is { } jsonNode)
            Json.Load(jsonNode, control);
        if (dictionary.TryGetNode("Reference") is { } referenceNode)
            Reference.Load(referenceNode, control);
    }

    public override object? GetValueAsObject() => Json.Value;
}
