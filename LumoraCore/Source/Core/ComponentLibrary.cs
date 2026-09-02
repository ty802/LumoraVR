// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lumora.Core;

// Built once from [ComponentCategory] attributes.
public static class ComponentLibrary
{
    public sealed class CategoryNode
    {
        public string Name = "";
        public string Path = "";
        public readonly SortedDictionary<string, CategoryNode> Subcategories = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<Type> Types = new();
    }

    public const string HiddenCategory = "Hidden";

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
                if (type == null || type.IsAbstract || !type.IsPublic)
                    continue;
                if (!typeof(Component).IsAssignableFrom(type))
                    continue;

                string category = type.GetCustomAttribute<ComponentCategoryAttribute>()?.Category ?? "Uncategorized";

                // "Hidden" is a component that exists for the engine's own use and has no business
                // being attachable from the browser - a save-file placeholder, say. Everything else
                // without a category still lands in Uncategorized. -xlinka
                if (category == HiddenCategory)
                    continue;

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

                // An open generic can't be attached, so the browser lists its declared closed forms
                // instead. One that declares none stays out entirely, which is the same as before.
                if (type.IsGenericTypeDefinition)
                {
                    foreach (var closed in GenericComponentTypes.Enumerate(type))
                        node.Types.Add(closed);
                    continue;
                }

                node.Types.Add(type);
            }
        }

        SortTypes(root);
        return root;
    }

    private static void SortTypes(CategoryNode node)
    {
        // Sorted by display name: every closed form of one generic shares the same Type.Name, so
        // sorting on that would leave them in whatever order reflection produced.
        node.Types.Sort((a, b) => string.CompareOrdinal(DisplayName(a), DisplayName(b)));
        foreach (var child in node.Subcategories.Values)
            SortTypes(child);
    }

    public static string DisplayName(Type type)
    {
        if (type == null)
            return "";
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];

        var args = type.GetGenericArguments();
        var argNames = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
            argNames[i] = DisplayName(args[i]);
        return $"{name}<{string.Join(", ", argNames)}>";
    }
}
