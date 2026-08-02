// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using Lumora.Core.Math;

namespace Lumora.Core;

public enum GenericTypeGroup
{
    Explicit,

    Values,

    WorldElements,
}

// Declares the type arguments a generic component can be attached with.
//
// A generic component class is not attachable by itself, only its closed forms are, so the browser
// and the type table need to know which ones exist up front. Without this the type never appears
// anywhere a person could pick it. -xlinka
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ComponentGenericTypesAttribute : Attribute
{
    public GenericTypeGroup Group { get; }
    public Type[] ExtraTypes { get; }

    public ComponentGenericTypesAttribute(GenericTypeGroup group, params Type[] extraTypes)
    {
        Group = group;
        ExtraTypes = extraTypes ?? Array.Empty<Type>();
    }

    public ComponentGenericTypesAttribute(params Type[] types)
    {
        Group = GenericTypeGroup.Explicit;
        ExtraTypes = types ?? Array.Empty<Type>();
    }
}

public static class GenericComponentTypes
{
    // Value types offered to generic components. Kept to what BOTH coders handle: a type the save
    // coder cannot write would attach fine and then throw on the first save, and one the sync coder
    // cannot encode would go over the wire as a null marker. -xlinka
    public static readonly Type[] ValueTypes =
    {
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(string),
        typeof(Uri),
        typeof(float2),
        typeof(float3),
        typeof(float4),
        typeof(floatQ),
        typeof(color),
        typeof(colorHDR),
    };

    public static readonly Type[] WorldElementTypes =
    {
        typeof(Slot),
        typeof(User),
    };

    // Empty for a generic component that never declared a set, and for arguments the definition's constraints
    // reject.
    public static IEnumerable<Type> Enumerate(Type definition)
    {
        if (definition == null || !definition.IsGenericTypeDefinition)
            yield break;
        if (definition.GetGenericArguments().Length != 1)
            yield break;

        var declaration = definition.GetCustomAttribute<ComponentGenericTypesAttribute>(inherit: false);
        if (declaration == null)
            yield break;

        foreach (var argument in Candidates(declaration))
        {
            var closed = TryClose(definition, argument);
            if (closed != null)
                yield return closed;
        }
    }

    private static IEnumerable<Type> Candidates(ComponentGenericTypesAttribute declaration)
    {
        switch (declaration.Group)
        {
            case GenericTypeGroup.Values:
                foreach (var type in ValueTypes)
                    yield return type;
                break;
            case GenericTypeGroup.WorldElements:
                foreach (var type in WorldElementTypes)
                    yield return type;
                break;
        }

        foreach (var type in declaration.ExtraTypes)
            yield return type;
    }

    // MakeGenericType is the constraint checker: it throws for an argument the definition refuses,
    // which beats re-implementing generic constraint resolution here.
    private static Type? TryClose(Type definition, Type argument)
    {
        if (argument == null)
            return null;
        try
        {
            return definition.MakeGenericType(argument);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
