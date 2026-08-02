// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lumora.Core;

public class HookTypeRegistry
{
    private Dictionary<Type, Type> _componentToHook = new Dictionary<Type, Type>();

    public void Register<TComponent, THook>()
        where TComponent : IImplementable
        where THook : IHook
    {
        Register(typeof(TComponent), typeof(THook));
    }

    public void Register(Type componentType, Type hookType)
    {
        if (!typeof(IImplementable).IsAssignableFrom(componentType))
        {
            throw new ArgumentException($"Component type {componentType} must implement IImplementable");
        }

        if (!typeof(IHook).IsAssignableFrom(hookType))
        {
            throw new ArgumentException($"Hook type {hookType} must implement IHook");
        }

        _componentToHook[componentType] = hookType;
    }

    // Exact type first, then up the inheritance chain. Null when nothing is registered.
    public Type GetHookType(Type componentType)
    {
        if (_componentToHook.TryGetValue(componentType, out Type? hookType))
            return hookType;

        var baseType = componentType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_componentToHook.TryGetValue(baseType, out hookType))
                return hookType;
            baseType = baseType.BaseType;
        }

        return null!;
    }

    public bool HasHook(Type componentType)
    {
        return GetHookType(componentType) != null;
    }

    public void Clear()
    {
        _componentToHook.Clear();
    }

    // Scan an assembly for hook classes tagged with [ImplementableHook(...)]
    // and register each declared target. Returns the number of registrations
    // added so callers can sanity-check at boot. - xlinka
    public int RegisterFromAssembly(Assembly assembly)
    {
        if (assembly == null) return 0;

        int registered = 0;
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(IHook).IsAssignableFrom(type))
                continue;

            var attrs = type.GetCustomAttributes(typeof(ImplementableHookAttribute), inherit: false);
            foreach (ImplementableHookAttribute attr in attrs)
            {
                foreach (var target in attr.Targets)
                {
                    if (target == null) continue;
                    if (!typeof(IImplementable).IsAssignableFrom(target)) continue;
                    Register(target, type);
                    registered++;
                }
            }
        }
        return registered;
    }
}
