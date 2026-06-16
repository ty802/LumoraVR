// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Persistence;

/// <summary>
/// How references that point outside the saved subtree are handled by a graph save.
/// </summary>
public enum DependencyHandling
{
    /// <summary>Null out every external reference — the subtree is saved entirely on its own.</summary>
    BreakExternal,

    /// <summary>
    /// Also collect the asset-provider components the subtree references (materials, meshes,
    /// textures, …) so the loaded object renders standalone in another world.
    /// </summary>
    CollectAssets,

    /// <summary>
    /// Collect every slot the subtree references (recursively) — a full, deep, cross-world copy.
    /// </summary>
    CollectAll,
}

/// <summary>
/// A serialized object graph produced by <see cref="Slot.SaveObject"/>: a dictionary holding the
/// root "Object", any collected "Assets", and "TypeVersions". Ready to write to a file/record or
/// load back into a world via <see cref="Slot.LoadObject"/>.
/// </summary>
public sealed class SavedGraph
{
    public DataTreeDictionary Root { get; }

    public SavedGraph(DataTreeDictionary root) => Root = root;

    /// <summary>Encode the graph to the binary data-tree format.</summary>
    public byte[] SaveToBytes() => DataTreeConverter.SaveToBytes(Root);
}
