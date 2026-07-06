// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Lumora.Core.Components;

/// <summary>
/// Reads/writes a nested struct member of a boxed value through a dotted field path ("x",
/// "baseColor.r", "" = the value itself). Lets non-generic editor components address one leaf of a
/// compound sync field: read the whole boxed value, mutate the leaf, write the whole value back.
/// </summary>
public sealed class StructMemberAccessor
{
    private static readonly ConcurrentDictionary<(Type, string), StructMemberAccessor> _cache = new();

    private readonly FieldInfo[] _chain;
    public Type LeafType { get; }

    public static StructMemberAccessor Get(Type rootType, string path)
        => _cache.GetOrAdd((rootType, path ?? ""), key => new StructMemberAccessor(key.Item1, key.Item2));

    private StructMemberAccessor(Type rootType, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            _chain = Array.Empty<FieldInfo>();
            LeafType = rootType;
            return;
        }

        var parts = path.Split('.');
        _chain = new FieldInfo[parts.Length];
        var current = rootType;
        for (int i = 0; i < parts.Length; i++)
        {
            var field = current.GetField(parts[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new ArgumentException($"No field '{parts[i]}' on {current.Name} (path '{path}' from {rootType.Name})");
            _chain[i] = field;
            current = field.FieldType;
        }
        LeafType = current;
    }

    public object? GetValue(object? root)
    {
        var value = root;
        for (int i = 0; i < _chain.Length && value != null; i++)
            value = _chain[i].GetValue(value);
        return value;
    }

    /// <summary>Set the leaf inside the boxed root; returns the (re)boxed root.</summary>
    public object? SetValue(object? root, object? leaf)
    {
        if (_chain.Length == 0)
            return leaf;
        if (root == null)
            return null;
        return SetRecursive(root, leaf, 0);
    }

    private object SetRecursive(object node, object? leaf, int depth)
    {
        var field = _chain[depth];
        if (depth == _chain.Length - 1)
        {
            field.SetValue(node, leaf); // boxed struct mutation is legal via FieldInfo
            return node;
        }
        var child = field.GetValue(node);
        if (child == null)
            return node;
        field.SetValue(node, SetRecursive(child, leaf, depth + 1));
        return node;
    }
}
