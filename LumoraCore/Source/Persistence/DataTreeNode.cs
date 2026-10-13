// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Persistence;

public enum DataTreeNodeType
{
    Value,
    List,
    Dictionary,
}

// The on-disk encoding is handled separately so the tree stays format-independent.
public abstract class DataTreeNode
{
    public abstract DataTreeNodeType NodeType { get; }

    // Depth first: this node, then its descendants in order. One walk with its own stack; nested iterators
    // passed every node up through each of its ancestors, which on a package of a few hundred thousand
    // slots was 13 s to find the assets it uses. -xlinka
    public IEnumerable<DataTreeNode> EnumerateTree()
    {
        var pending = new Stack<DataTreeNode>();
        var children = new List<DataTreeNode>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            children.Clear();
            if (node is DataTreeDictionary dictionary)
                children.AddRange(dictionary.Children.Values);
            else if (node is DataTreeList list)
                children.AddRange(list.Children);
            for (int i = children.Count - 1; i >= 0; i--)
                pending.Push(children[i]);
        }
    }
}
