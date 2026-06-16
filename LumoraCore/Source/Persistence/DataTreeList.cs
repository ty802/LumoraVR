// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

/// <summary>An ordered sequence of child nodes (e.g. a slot's children, a sync list's elements).</summary>
public sealed class DataTreeList : DataTreeNode, IEnumerable<DataTreeNode>
{
    public override DataTreeNodeType NodeType => DataTreeNodeType.List;

    public List<DataTreeNode> Children { get; } = new();

    public int Count => Children.Count;

    public DataTreeNode this[int index] => Children[index];

    public void Add(DataTreeNode node) => Children.Add(node);

    public override IEnumerable<DataTreeNode> EnumerateTree()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.EnumerateTree())
                yield return node;
    }

    public IEnumerator<DataTreeNode> GetEnumerator() => Children.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"DataTreeList ({Children.Count})";
}
