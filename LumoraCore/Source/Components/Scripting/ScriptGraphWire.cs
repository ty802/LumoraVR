// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Scripting;

// One wire of an unpacked graph, kept by the end that may only have one: a data input names the output
// it reads, a flow exit names the node it runs next. -xlinka
public sealed class ScriptGraphWire : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    // This node's end: the input for a data wire, the exit for a flow wire.
    public readonly Sync<string> Port = new();

    public readonly SyncRef<ScriptGraphNode> Node = new();

    // The other node's end: its output for a data wire, its flow input for a flow wire.
    public readonly Sync<string> NodePort = new();

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
        dictionary.Add("Node", Node.Save(control));
        dictionary.Add("NodePort", NodePort.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Port") is { } portNode)
            Port.Load(portNode, control);
        if (dictionary.TryGetNode("Node") is { } targetNode)
            Node.Load(targetNode, control);
        if (dictionary.TryGetNode("NodePort") is { } nodePortNode)
            NodePort.Load(nodePortNode, control);
    }

    public override object? GetValueAsObject() => Port.Value;
}
