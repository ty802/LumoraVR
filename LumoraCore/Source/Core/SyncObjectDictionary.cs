// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

/// <summary>
/// A keyed, network-synchronized collection of world-element members: each value is a real sub-worker
/// with its own RefID that syncs and persists itself (contrast with <see cref="SyncValueDictionary{TKey,TValue}"/>,
/// which stores plain values). Implemented as an element list of key/value entry elements - values are
/// created by the collection (<see cref="Add(TKey)"/>), and a missing key returns null. Lookups scan the
/// entries, so this suits modest keyed collections rather than very large maps.
/// </summary>
public class SyncObjectDictionary<TKey, TValue> : SyncList<SyncObjectDictionary<TKey, TValue>.Entry>
    where TKey : notnull
    where TValue : class, ISyncMember, new()
{
    /// <summary>
    /// One key/value pair. A composite element whose key field and value element each sync and persist
    /// themselves; the container carries no payload of its own (same idiom as other composite elements).
    /// </summary>
    public sealed class Entry : SyncElement
    {
        public override SyncMemberType MemberType => SyncMemberType.Object;

        public readonly Sync<TKey> Key = new();
        public readonly TValue Value = new();

        public override void Initialize(World world, IWorldElement? parent)
        {
            base.Initialize(world, parent);
            SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        }

        // Children sync as their own SyncElements; the container has no payload.
        protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
        protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
        protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
        protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
        protected override void InternalClearDirty() { }

        public override DataTreeNode Save(SaveControl control)
        {
            var dictionary = new DataTreeDictionary();
            dictionary.Add("Key", Key.Save(control));
            dictionary.Add("Value", Value.Save(control));
            return dictionary;
        }

        public override void Load(DataTreeNode node, LoadControl control)
        {
            if (node is not DataTreeDictionary dictionary)
                return;
            if (dictionary.TryGetNode("Key") is { } keyNode)
                Key.Load(keyNode, control);
            if (dictionary.TryGetNode("Value") is { } valueNode)
                Value.Load(valueNode, control);
        }

        public override object? GetValueAsObject() => Key.Value;
    }

    /// <summary>Get the value for a key, or create a new value element if the key is absent.</summary>
    public TValue GetOrAdd(TKey key)
    {
        var existing = FindEntry(key);
        if (existing != null)
            return existing.Value;
        return Add(key);
    }

    /// <summary>Add a new value element under <paramref name="key"/>. If the key already exists, returns
    /// the existing value rather than creating a duplicate.</summary>
    public TValue Add(TKey key)
    {
        var existing = FindEntry(key);
        if (existing != null)
            return existing.Value;

        var entry = base.Add();
        entry.Key.Value = key;
        return entry.Value;
    }

    public bool ContainsKey(TKey key) => FindEntry(key) != null;

    public bool TryGetValue(TKey key, out TValue value)
    {
        var entry = FindEntry(key);
        value = entry?.Value!;
        return entry != null;
    }

    public TValue? this[TKey key] => FindEntry(key)?.Value;

    public bool Remove(TKey key)
    {
        int index = FindIndex(e => EqualityComparer<TKey>.Default.Equals(e.Key.Value, key));
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    public IEnumerable<TKey> Keys
    {
        get
        {
            foreach (var entry in Elements)
                yield return entry.Key.Value;
        }
    }

    public IEnumerable<TValue> Values
    {
        get
        {
            foreach (var entry in Elements)
                yield return entry.Value;
        }
    }

    private Entry? FindEntry(TKey key)
    {
        foreach (var entry in Elements)
        {
            if (EqualityComparer<TKey>.Default.Equals(entry.Key.Value, key))
                return entry;
        }
        return null;
    }
}
