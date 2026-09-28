// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;
using Lumora.Core.Scripting;

namespace Lumora.Core.Components.Scripting;

// One value a Shared script keeps on its host for every machine: its name (Key), the script type it was
// written as, and the value as text. Only the host writes any of it. What it last read as is kept, so a
// script reading it every frame parses it once per change. -xlinka
public sealed class ScriptSyncedValue : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<string> Key = new();
    public readonly Sync<string> Type = new();
    public readonly Sync<string> Value = new();

    private string? _readText;
    private string? _readType;
    private System.Type? _readAs;
    private object? _read;

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        Key.MarkHostOnly();
        Type.MarkHostOnly();
        Value.MarkHostOnly();
    }

    public bool TryRead<T>(out T value)
    {
        string text = Value.Value ?? string.Empty;
        string type = Type.Value ?? string.Empty;
        if (!ReferenceEquals(text, _readText) || !ReferenceEquals(type, _readType) || _readAs != typeof(T))
        {
            _readText = text;
            _readType = type;
            _readAs = typeof(T);
            _read = ScriptSynced.TryParse<T>(type, text, out var parsed) ? parsed : null;
        }
        if (_read is T hit)
        {
            value = hit;
            return true;
        }
        value = default!;
        return false;
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Key", Key.Save(control));
        dictionary.Add("Type", Type.Save(control));
        dictionary.Add("Value", Value.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Key") is { } keyNode)
            Key.Load(keyNode, control);
        if (dictionary.TryGetNode("Type") is { } typeNode)
            Type.Load(typeNode, control);
        if (dictionary.TryGetNode("Value") is { } valueNode)
            Value.Load(valueNode, control);
    }

    public override object? GetValueAsObject() => Value.Value;
}
