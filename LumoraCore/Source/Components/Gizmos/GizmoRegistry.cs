// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumora.Core.Logging;

namespace Lumora.Core.Components.Gizmos;

// live gizmo instances are tracked in WorldGizmos, one table per world; this only maps component types to gizmo types
public static class GizmoRegistry
{
    private static readonly Dictionary<Type, Type> _gizmoTypes = new();
    private static bool _initialized = false;

    public static void Initialize()
    {
        if (_initialized) return;

        _gizmoTypes.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                ScanAssembly(assembly);
            }
            catch (Exception ex)
            {
                Logger.Warn($"GizmoRegistry: Failed to scan assembly {assembly.FullName}: {ex.Message}");
            }
        }

        _initialized = true;
        Logger.Log($"GizmoRegistry: Initialized with {_gizmoTypes.Count} gizmo types");
    }

    private static void ScanAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<GizmoForComponentAttribute>();
            if (attr != null)
            {
                RegisterGizmo(attr.ComponentType, type);
            }
        }
    }

    public static void RegisterGizmo(Type componentType, Type gizmoType)
    {
        if (componentType == null)
            throw new ArgumentNullException(nameof(componentType));
        if (gizmoType == null)
            throw new ArgumentNullException(nameof(gizmoType));

        if (_gizmoTypes.ContainsKey(componentType))
        {
            Logger.Warn($"GizmoRegistry: Replacing gizmo for {componentType.Name} with {gizmoType.Name}");
        }

        _gizmoTypes[componentType] = gizmoType;
        Logger.Log($"GizmoRegistry: Registered {gizmoType.Name} for {componentType.Name}");
    }

    public static Type GetGizmoType(Type componentType)
    {
        if (!_initialized) Initialize();

        if (_gizmoTypes.TryGetValue(componentType, out var gizmoType))
            return gizmoType;

        var baseType = componentType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_gizmoTypes.TryGetValue(baseType, out gizmoType))
                return gizmoType;
            baseType = baseType.BaseType;
        }

        return null!;
    }

    // Filtered rather than kept in a second table: the map also holds the slot gizmo, which is not a
    // component gizmo and must never be spawned onto a component. The base-type walk in
    // GetGizmoType is what makes one registration cover a family - a renderer gizmo
    // registered for MeshRenderer also serves the skinned one. -xlinka
    public static Type? GetComponentGizmoType(Type componentType)
    {
        if (componentType == null)
            return null;
        var gizmoType = GetGizmoType(componentType);
        if (gizmoType == null || !typeof(ComponentGizmo).IsAssignableFrom(gizmoType) || gizmoType.IsAbstract)
            return null;
        return gizmoType;
    }

    public static bool HasComponentGizmo(Type componentType) => GetComponentGizmoType(componentType) != null;

    public static IEnumerable<(Type ComponentType, Type GizmoType)> GetAllRegistered()
    {
        if (!_initialized) Initialize();
        return _gizmoTypes.Select(kvp => (kvp.Key, kvp.Value));
    }
}
