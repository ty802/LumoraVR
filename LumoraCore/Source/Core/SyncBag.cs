// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

/// <summary>
/// Compact synchronized unordered collection.
/// Stores values in a List&lt;T&gt; backing — allows duplicates, no ordering guarantees.
/// Supports full and op-log delta network encoding via SyncCoder.
/// Use this instead of SyncFieldList&lt;T&gt; for unordered bag semantics.
/// </summary>
public class SyncBag<T> : ConflictingSyncElement, IEnumerable<T>
{
    private const byte OpAdd = 0;
    private const byte OpRemove = 1;
    private const byte OpClear = 2;

    private struct BagOp
    {
        public byte Type;
        public T Value;
    }

    private readonly List<T> _items = new();
    private List<BagOp>? _pendingOps;

    public int Count
    {
        get
        {
            AuthorizeDataModelAccess(DataModelPermissionAction.Read | DataModelPermissionAction.CollectionEnumerate, DataModelPermissionSurface.Bag);
            return _items.Count;
        }
    }

    public event Action<SyncBag<T>>? OnChanged;

    public void Add(T item)
    {
        if (!AuthorizeDataModelMutation(DataModelPermissionAction.Write | DataModelPermissionAction.CollectionAdd, DataModelPermissionSurface.Bag, key: item))
            return;
        if (!BeginModification()) return;
        _items.Add(item);
        (_pendingOps ??= new List<BagOp>()).Add(new BagOp { Type = OpAdd, Value = item });
        InvalidateSyncElement();
        BlockModification();
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
    }

    public bool Remove(T item)
    {
        int idx = _items.IndexOf(item);
        if (idx < 0) return false;
        if (!AuthorizeDataModelMutation(DataModelPermissionAction.Write | DataModelPermissionAction.CollectionRemove, DataModelPermissionSurface.Bag, index: idx, key: item))
            return false;
        if (!BeginModification()) return false;
        _items.RemoveAt(idx);
        (_pendingOps ??= new List<BagOp>()).Add(new BagOp { Type = OpRemove, Value = item });
        InvalidateSyncElement();
        BlockModification();
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
        return true;
    }

    public bool Contains(T item)
    {
        AuthorizeDataModelAccess(DataModelPermissionAction.Read | DataModelPermissionAction.CollectionEnumerate, DataModelPermissionSurface.Bag, key: item);
        return _items.Contains(item);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        if (!AuthorizeDataModelMutation(DataModelPermissionAction.Write | DataModelPermissionAction.CollectionClear, DataModelPermissionSurface.Bag))
            return;
        if (!BeginModification()) return;
        _items.Clear();
        _pendingOps?.Clear();
        (_pendingOps ??= new List<BagOp>()).Add(new BagOp { Type = OpClear });
        InvalidateSyncElement();
        BlockModification();
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write7BitEncoded((ulong)_items.Count);
        foreach (var item in _items)
            SyncCoder.Encode(writer, item);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        _items.Clear();
        int count = (int)reader.Read7BitEncoded();
        for (int i = 0; i < count; i++)
            _items.Add(SyncCoder.Decode<T>(reader));
        BlockModification();
        OnChanged?.Invoke(this);
        UnblockModification();
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        if (_pendingOps == null || _pendingOps.Count == 0)
        {
            writer.Write(true); // isFull flag
            InternalEncodeFull(writer, outboundMessage);
            return;
        }
        writer.Write(false); // isFull flag
        writer.Write7BitEncoded((ulong)_pendingOps.Count);
        foreach (var op in _pendingOps)
        {
            writer.Write(op.Type);
            if (op.Type == OpAdd || op.Type == OpRemove)
                SyncCoder.Encode(writer, op.Value);
        }
    }

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        bool isFull = reader.ReadBoolean();
        if (isFull)
        {
            InternalDecodeFull(reader, inboundMessage);
            return;
        }
        int opCount = (int)reader.Read7BitEncoded();
        bool changed = false;
        for (int i = 0; i < opCount; i++)
        {
            byte type = reader.ReadByte();
            if (type == OpAdd)
            {
                _items.Add(SyncCoder.Decode<T>(reader));
                changed = true;
            }
            else if (type == OpRemove)
            {
                T value = SyncCoder.Decode<T>(reader);
                _items.Remove(value);
                changed = true;
            }
            else if (type == OpClear)
            {
                _items.Clear();
                changed = true;
            }
        }
        if (changed)
        {
            BlockModification();
            OnChanged?.Invoke(this);
            UnblockModification();
        }
    }

    public override MessageValidity Validate(BinaryMessageBatch syncMessage, BinaryReader reader, List<ValidationGroup.Rule> rules)
    {
        var validity = base.Validate(syncMessage, reader, rules);
        if (validity != MessageValidity.Valid || World?.IsAuthority != true)
        {
            return validity;
        }

        long position = reader.BaseStream.CanSeek ? reader.BaseStream.Position : -1;
        try
        {
            bool isFull = reader.ReadBoolean();
            if (isFull)
            {
                int count = (int)reader.Read7BitEncoded();
                for (int i = 0; i < count; i++)
                {
                    var value = SyncCoder.Decode<T>(reader);
                    if (!AuthorizeDataModelMutation(
                            DataModelPermissionAction.Write | DataModelPermissionAction.CollectionSet | DataModelPermissionAction.Replicate,
                            DataModelPermissionSurface.Bag,
                            syncMessage.SenderUser,
                            isNetwork: true,
                            key: value,
                            throwOnError: false))
                    {
                        return MessageValidity.Conflict;
                    }
                }
                return MessageValidity.Valid;
            }

            int opCount = (int)reader.Read7BitEncoded();
            for (int i = 0; i < opCount; i++)
            {
                byte type = reader.ReadByte();
                if (type == OpAdd)
                {
                    var value = SyncCoder.Decode<T>(reader);
                    if (!AuthorizeDataModelMutation(
                            DataModelPermissionAction.Write | DataModelPermissionAction.CollectionAdd | DataModelPermissionAction.Replicate,
                            DataModelPermissionSurface.Bag,
                            syncMessage.SenderUser,
                            isNetwork: true,
                            key: value,
                            throwOnError: false))
                    {
                        return MessageValidity.Conflict;
                    }
                }
                else if (type == OpRemove)
                {
                    var value = SyncCoder.Decode<T>(reader);
                    if (!AuthorizeDataModelMutation(
                            DataModelPermissionAction.Write | DataModelPermissionAction.CollectionRemove | DataModelPermissionAction.Replicate,
                            DataModelPermissionSurface.Bag,
                            syncMessage.SenderUser,
                            isNetwork: true,
                            key: value,
                            throwOnError: false))
                    {
                        return MessageValidity.Conflict;
                    }
                }
                else if (type == OpClear)
                {
                    if (!AuthorizeDataModelMutation(
                            DataModelPermissionAction.Write | DataModelPermissionAction.CollectionClear | DataModelPermissionAction.Replicate,
                            DataModelPermissionSurface.Bag,
                            syncMessage.SenderUser,
                            isNetwork: true,
                            throwOnError: false))
                    {
                        return MessageValidity.Conflict;
                    }
                }
                else
                {
                    return MessageValidity.Conflict;
                }
            }

            return MessageValidity.Valid;
        }
        catch
        {
            return MessageValidity.Conflict;
        }
        finally
        {
            if (position >= 0)
            {
                reader.BaseStream.Position = position;
            }
        }
    }

    protected override void InternalClearDirty()
    {
        _pendingOps?.Clear();
    }

    public override object? GetValueAsObject() => null;

    public IEnumerator<T> GetEnumerator()
    {
        AuthorizeDataModelAccess(DataModelPermissionAction.Read | DataModelPermissionAction.CollectionEnumerate, DataModelPermissionSurface.Bag);
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override void Dispose()
    {
        OnChanged = null;
        _items.Clear();
        _pendingOps?.Clear();
        _pendingOps = null;
        base.Dispose();
    }
}
