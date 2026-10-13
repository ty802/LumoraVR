// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

public sealed class DataTreeList : DataTreeNode, IEnumerable<DataTreeNode>
{
    public override DataTreeNodeType NodeType => DataTreeNodeType.List;

    public List<DataTreeNode> Children { get; } = new();

    public int Count => Children.Count;

    public DataTreeNode this[int index] => Children[index];

    public void Add(DataTreeNode node) => Children.Add(node);

    public IEnumerator<DataTreeNode> GetEnumerator() => Children.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"DataTreeList ({Children.Count})";
}
