using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

/// <summary>
/// Compact synchronized dictionary for value types and primitives.
/// Supports full and op-log delta network encoding via SyncCoder.
/// Use this instead of wrapping individual Sync&lt;T&gt; fields for keyed collections.
/// </summary>
public class SyncDictionary<TKey, TValue> : ConflictingSyncElement, IEnumerable<KeyValuePair<TKey, TValue>>
{
    private const byte OpSet = 0;
    private const byte OpRemove = 1;
    private const byte OpClear = 2;

    private struct DictOp
    {
        public byte Type;
        public TKey Key;
        public TValue Value;
    }

    private readonly Dictionary<TKey, TValue> _dict = new();
    private List<DictOp>? _pendingOps;

    public int Count => _dict.Count;
    public IEnumerable<TKey> Keys => _dict.Keys;
    public IEnumerable<TValue> Values => _dict.Values;

    public event Action<SyncDictionary<TKey, TValue>>? OnChanged;
    public event Action<TKey, TValue>? OnKeyChanged;
    public event Action<TKey>? OnKeyRemoved;

    public TValue this[TKey key]
    {
        get => _dict[key];
        set => SetKey(key, value);
    }

    public void Add(TKey key, TValue value)
    {
        if (!BeginModification()) return;
        _dict.Add(key, value);
        (_pendingOps ??= new List<DictOp>()).Add(new DictOp { Type = OpSet, Key = key, Value = value });
        InvalidateSyncElement();
        BlockModification();
        OnKeyChanged?.Invoke(key, value);
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
    }

    public bool TryAdd(TKey key, TValue value)
    {
        if (_dict.ContainsKey(key)) return false;
        Add(key, value);
        return true;
    }

    public bool Remove(TKey key)
    {
        if (!_dict.ContainsKey(key)) return false;
        if (!BeginModification()) return false;
        _dict.Remove(key);
        (_pendingOps ??= new List<DictOp>()).Add(new DictOp { Type = OpRemove, Key = key });
        InvalidateSyncElement();
        BlockModification();
        OnKeyRemoved?.Invoke(key);
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
        return true;
    }

    public bool ContainsKey(TKey key) => _dict.ContainsKey(key);
    public bool ContainsValue(TValue value) => _dict.ContainsValue(value);
    public bool TryGetValue(TKey key, out TValue value) => _dict.TryGetValue(key, out value);

    public TValue GetValueOrDefault(TKey key, TValue defaultValue = default!)
        => _dict.TryGetValue(key, out var v) ? v : defaultValue;

    public void Clear()
    {
        if (_dict.Count == 0) return;
        if (!BeginModification()) return;
        _dict.Clear();
        _pendingOps?.Clear();
        (_pendingOps ??= new List<DictOp>()).Add(new DictOp { Type = OpClear });
        InvalidateSyncElement();
        BlockModification();
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
    }

    private void SetKey(TKey key, TValue value)
    {
        if (!BeginModification()) return;
        _dict[key] = value;
        (_pendingOps ??= new List<DictOp>()).Add(new DictOp { Type = OpSet, Key = key, Value = value });
        InvalidateSyncElement();
        BlockModification();
        OnKeyChanged?.Invoke(key, value);
        OnChanged?.Invoke(this);
        UnblockModification();
        EndModification();
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write7BitEncoded((ulong)_dict.Count);
        foreach (var kv in _dict)
        {
            SyncCoder.Encode(writer, kv.Key);
            SyncCoder.Encode(writer, kv.Value);
        }
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        _dict.Clear();
        int count = (int)reader.Read7BitEncoded();
        for (int i = 0; i < count; i++)
        {
            var key = SyncCoder.Decode<TKey>(reader);
            var value = SyncCoder.Decode<TValue>(reader);
            _dict[key] = value;
        }
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
            if (op.Type == OpSet)
            {
                SyncCoder.Encode(writer, op.Key);
                SyncCoder.Encode(writer, op.Value);
            }
            else if (op.Type == OpRemove)
            {
                SyncCoder.Encode(writer, op.Key);
            }
            // OpClear: no payload
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
        bool anyChanged = false;
        for (int i = 0; i < opCount; i++)
        {
            byte type = reader.ReadByte();
            if (type == OpSet)
            {
                var key = SyncCoder.Decode<TKey>(reader);
                var value = SyncCoder.Decode<TValue>(reader);
                _dict[key] = value;
                BlockModification();
                OnKeyChanged?.Invoke(key, value);
                UnblockModification();
                anyChanged = true;
            }
            else if (type == OpRemove)
            {
                var key = SyncCoder.Decode<TKey>(reader);
                _dict.Remove(key);
                BlockModification();
                OnKeyRemoved?.Invoke(key);
                UnblockModification();
                anyChanged = true;
            }
            else if (type == OpClear)
            {
                _dict.Clear();
                anyChanged = true;
            }
        }
        if (anyChanged)
        {
            BlockModification();
            OnChanged?.Invoke(this);
            UnblockModification();
        }
    }

    protected override void InternalClearDirty()
    {
        _pendingOps?.Clear();
    }

    public override object? GetValueAsObject() => null;

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _dict.GetEnumerator();

    public override void Dispose()
    {
        OnChanged = null;
        OnKeyChanged = null;
        OnKeyRemoved = null;
        _dict.Clear();
        _pendingOps?.Clear();
        _pendingOps = null;
        base.Dispose();
    }
}
