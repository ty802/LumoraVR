// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

// A field holding a Type. Not to be confused with SyncMemberType, which names the KIND of a member.
//
// The value travels as a name, never as a runtime handle: a handle means nothing to another peer or
// to a file. Over the wire that goes through the world's type index table, so a type used by many
// members costs one index after its first mention; in a save it is the plain full name, resolved back
// through the same lookup that handles renamed types. A name this build cannot resolve loads as null
// rather than throwing - the honest answer for a client missing an assembly is an empty field, not a
// dead world. -xlinka
public class SyncType : SyncField<Type>
{
    public override Type Value
    {
        get => base.Value;
        set
        {
            if (value == base.Value)
                return;
            base.Value = value;
        }
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        var value = Value;
        writer.Write(value != null);
        if (value != null)
            World.Workers.EncodeType(writer, value);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        Type decoded = reader.ReadBoolean() ? World.Workers.DecodeType(reader) : null!;
        InternalSetValue(in decoded, sync: false);
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        InternalEncodeFull(writer, outboundMessage);
    }

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        InternalDecodeFull(reader, inboundMessage);
    }

    public override DataTreeNode Save(SaveControl control) => new DataTreeValue(Value?.FullName);

    public override void Load(DataTreeNode node, LoadControl control)
    {
        string? name = node is DataTreeValue { IsNull: false } value ? value.Extract<string>() : null;
        Type resolved = string.IsNullOrEmpty(name) ? null! : WorkerManager.GetType(name!);
        InternalSetValue(in resolved, sync: false, change: false);
    }

    public override string ToString() => Value?.FullName ?? "<null>";
}
