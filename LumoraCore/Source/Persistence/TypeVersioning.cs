// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Lumora.Core.Persistence;

// Stamps a serialization version onto a worker type. A save records the version of every stamped type
// it wrote, and the loader hands that number back through LoadControl.GetTypeVersion so the type's own
// Load can branch on which shape it is reading.
//
// Bump it when the MEANING of existing member data changes and the type has to reinterpret it. Adding
// or removing a member needs no bump: Worker.Load already tolerates both (an absent key keeps the
// constructed default, an unknown key is ignored).
//
// Unstamped types cost nothing anywhere - no attribute, no version key in the file, GetTypeVersion
// answers 0. -xlinka
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SaveTypeVersionAttribute : Attribute
{
    public int Version { get; }

    public SaveTypeVersionAttribute(int version)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), "A save type version starts at 1; 0 means unversioned.");
        Version = version;
    }
}

public static class TypeVersioning
{
    // Attribute lookups are reflection, and the save walk hits every worker in the world, so the answer
    // is memoised per type. Almost every entry is 0.
    private static readonly ConcurrentDictionary<Type, int> _versions = new();

    public static int GetDeclaredVersion(Type type)
    {
        if (type == null)
            return 0;
        if (_versions.TryGetValue(type, out var version))
            return version;
        version = type.GetCustomAttribute<SaveTypeVersionAttribute>(inherit: false)?.Version ?? 0;
        _versions[type] = version;
        return version;
    }

    public static int GetDeclaredVersion<T>() => GetDeclaredVersion(typeof(T));
}
