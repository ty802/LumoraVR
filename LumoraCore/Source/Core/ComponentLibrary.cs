// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lumora.Core;

/// <summary>
/// Category tree of every attachable component type, built once from [ComponentCategory] attributes.
/// The inspector's component browser walks this.
/// </summary>
public static class ComponentLibrary
{
    public sealed class CategoryNode
    {
        public string Name = "";
        public string Path = "";
        public readonly SortedDictionary<string, CategoryNode> Subcategories = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Type> Types = new();
    }

    private static CategoryNode? _root;
    private static readonly object _buildLock = new();

    public static CategoryNode Root
    {
        get
        {
            if (_root == null)
            {
                lock (_buildLock)
                    _root ??= Build();
            }
            return _root;
        }
    }

    /// <summary>Resolve a "Physics/Colliders"-style path to its node (null when absent).</summary>
    public static CategoryNode? GetNode(string path)
    {
        var node = Root;
        if (string.IsNullOrEmpty(path))
            return node;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.Subcategories.TryGetValue(part, out node!))
                return null;
        }
        return node;
    }

    private static CategoryNode Build()
    {
        var root = new CategoryNode();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types!; }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || !type.IsPublic || type.IsGenericTypeDefinition)
                    continue;
                if (!typeof(Component).IsAssignableFrom(type))
                    continue;

                string category = type.GetCustomAttribute<ComponentCategoryAttribute>()?.Category ?? "Uncategorized";
                var node = root;
                foreach (var part in category.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!node.Subcategories.TryGetValue(part, out var child))
                    {
                        child = new CategoryNode { Name = part, Path = node.Path.Length == 0 ? part : node.Path + "/" + part };
                        node.Subcategories[part] = child;
                    }
                    node = child;
                }
                node.Types.Add(type);
            }
        }

        SortTypes(root);
        return root;
    }

    private static void SortTypes(CategoryNode node)
    {
        node.Types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var child in node.Subcategories.Values)
            SortTypes(child);
    }
}
