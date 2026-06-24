// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

/// <summary>
/// A keyed map of child nodes - the workhorse of the format. A component serializes its members
/// into one of these (member name -> value/sub-tree); a world serializes its sections likewise.
/// </summary>
public sealed class DataTreeDictionary : DataTreeNode
{
    public override DataTreeNodeType NodeType => DataTreeNodeType.Dictionary;

    public Dictionary<string, DataTreeNode> Children { get; } = new();

    public DataTreeNode this[string key]
        => Children.TryGetValue(key, out var node)
            ? node
            : throw new KeyNotFoundException($"DataTreeDictionary has no key '{key}'");

    public bool ContainsKey(string key) => Children.ContainsKey(key);

    public bool Remove(string key) => Children.Remove(key);

    // ADD

    /// <summary>Add a child node under <paramref name="key"/>.</summary>
    public void Add(string key, DataTreeNode node) => Children.Add(key, node);

    /// <summary>Add a primitive value under <paramref name="key"/> (wrapped in a <see cref="DataTreeValue"/>).</summary>
    public void Add<T>(string key, T value) => Children.Add(key, MakeValue(value));

    public void AddOrUpdate(string key, DataTreeNode node)
    {
        Children.Remove(key);
        Children.Add(key, node);
    }

    public void AddOrUpdate<T>(string key, T value)
    {
        Children.Remove(key);
        Children.Add(key, MakeValue(value));
    }

    // GET (nodes)

    public DataTreeNode? TryGetNode(string key) => Children.GetValueOrDefault(key);
    public DataTreeList? TryGetList(string key) => Children.GetValueOrDefault(key) as DataTreeList;
    public DataTreeDictionary? TryGetDictionary(string key) => Children.GetValueOrDefault(key) as DataTreeDictionary;

    // EXTRACT (primitives)

    public bool TryExtract<T>(string key, ref T value)
    {
        if (Children.TryGetValue(key, out var node))
        {
            value = ExtractValue<T>(node);
            return true;
        }
        return false;
    }

    public T ExtractOrDefault<T>(string key, T fallback = default!)
        => Children.TryGetValue(key, out var node) ? ExtractValue<T>(node) : fallback;

    public T ExtractOrThrow<T>(string key)
        => Children.TryGetValue(key, out var node)
            ? ExtractValue<T>(node)
            : throw new KeyNotFoundException($"DataTreeDictionary has no key '{key}'");

    private static DataTreeNode MakeValue<T>(T value) => value switch
    {
        null => new DataTreeValue((IConvertible?)null),
        // Already a tree node (e.g. SaveReference's DataTreeValue, or a sub-list/dict) - store as-is.
        DataTreeNode node => node,
        Uri uri => new DataTreeValue(uri),
        string str => new DataTreeValue(str),
        IConvertible convertible => new DataTreeValue(convertible),
        _ => throw new NotSupportedException(
            $"Cannot store '{typeof(T)}' as a primitive value; add it as a DataTreeNode (dictionary/list)."),
    };

    private static T ExtractValue<T>(DataTreeNode node)
    {
        if (node is DataTreeValue value)
            return value.Extract<T>();
        throw new InvalidOperationException($"Node is a {node.NodeType}, not a primitive value.");
    }

    public override IEnumerable<DataTreeNode> EnumerateTree()
    {
        yield return this;
        foreach (var child in Children.Values)
            foreach (var node in child.EnumerateTree())
                yield return node;
    }

    public override string ToString() => $"DataTreeDictionary ({Children.Count})";
}
