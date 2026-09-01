// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Persistence;

// A member that holds a slice of a save file verbatim and hands it back untouched.
//
// It exists for data this build cannot interpret. Everything else in the datamodel decodes what it is
// given into typed elements, which is exactly the wrong move here: decoding is what loses the parts we
// do not understand. So the node goes in as it came off disk and comes out the same, which is what
// makes a load/save round trip of unknown content lossless rather than lossy.
//
// IT DOES NOT REPLICATE, ON PURPOSE. The encode/decode pair is empty, so this member contributes
// nothing to a full state or a delta. A peer's copy is simply empty, and a peer that cannot resolve the
// original type is left exactly where it is today - without it - rather than being handed a blob it has
// no way to check, size-bound or make sense of. Preservation is a local-file guarantee, not a network
// one. -xlinka
public sealed class SyncRawData : SyncElement, ISyncMemberCopy
{
    private DataTreeNode? _node;

    public override SyncMemberType MemberType => SyncMemberType.Object;

    public DataTreeNode? Node => _node;

    public bool IsEmpty => _node == null;

    public void SetNode(DataTreeNode? node) => _node = node;

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    // A null payload still writes a node, so the member's absence in the file means "written by a build
    // before this member existed" and not "written empty".
    public override DataTreeNode Save(SaveControl control)
        => _node ?? new DataTreeValue((IConvertible?)null);

    public override void Load(DataTreeNode node, LoadControl control)
        => _node = node is DataTreeValue { IsNull: true } ? null : node;

    // The node is only ever read, never edited in place, so the clone shares it instead of deep-copying
    // a tree that can be the whole member set of a component.
    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is SyncRawData other && !ReferenceEquals(other, this))
            _node = other._node;
    }

    public override object? GetValueAsObject() => _node;

    public override string ToString() => _node == null ? "<empty>" : $"raw {_node.NodeType}";
}
