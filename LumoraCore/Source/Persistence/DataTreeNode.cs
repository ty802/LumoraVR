// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Persistence;

/// <summary>The kind of a <see cref="DataTreeNode"/>.</summary>
public enum DataTreeNodeType
{
    Value,
    List,
    Dictionary,
}

/// <summary>
/// A node in a serialized data tree: a primitive <see cref="DataTreeValue"/>, an ordered
/// <see cref="DataTreeList"/>, or a keyed <see cref="DataTreeDictionary"/>. This is the structured
/// in-memory form that worlds and components serialize into; the on-disk encoding is handled
/// separately so the tree stays format-independent.
/// </summary>
public abstract class DataTreeNode
{
    public abstract DataTreeNodeType NodeType { get; }

    /// <summary>Depth-first enumeration of this node and all descendants.</summary>
    public abstract IEnumerable<DataTreeNode> EnumerateTree();
}
