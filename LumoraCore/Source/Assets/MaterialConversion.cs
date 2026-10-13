// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Carries a material's look across to a material of another kind. First every property both kinds have
// under the same name and type, counting the ones a kind exposes as plain properties (the shared Color
// and MainTexture every common material has, a specular material's Smoothness packed into its specular
// alpha), then the ones each kind keeps under a name of its own: how the main texture is laid over the
// surface, the normal map and its strength, the glow. A PBS albedo lands on an unlit tint, an unlit tint
// on a toon base colour, and so on. -xlinka
public static class MaterialConversion
{
    // The names one role goes by across the material kinds, most common first. Colour and texture are
    // here for the kinds with no shared Color and MainTexture.
    private static readonly string[][] Roles =
    {
        new[] { "AlbedoColor", "TintColor", "Color", "BaseColor", "MainColor" },
        new[] { "AlbedoTexture", "Texture", "MainTexture", "BaseTexture" },
        new[] { "TextureScale", "AlbedoTextureScale", "MainTextureScale" },
        new[] { "TextureOffset", "AlbedoTextureOffset", "MainTextureOffset" },
        new[] { "NormalMap", "NormalTexture" },
        new[] { "NormalScale", "NormalStrength", "NormalIntensity" },
        new[] { "EmissiveColor", "EmissionColor" },
        new[] { "EmissiveMap", "EmissionMap", "EmissiveTexture" },
    };

    // What every component carries and no material's look depends on.
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "persistent", "Enabled", "updateOrder" };

    private sealed class Member
    {
        public required Type Kind { get; init; }
        public required bool IsProperty { get; init; }
        public required Type ValueType { get; init; }
        public required Func<object?> Get { get; init; }
        public required Action<object?>? Set { get; init; }
    }

    // Returns how many properties came across.
    public static int CopyProperties(MaterialProvider from, MaterialProvider to)
    {
        if (from == null || to == null || ReferenceEquals(from, to))
            return 0;
        var source = Members(from);
        var target = Members(to);
        int copied = 0;

        foreach (var (name, member) in target)
        {
            if (!Skipped.Contains(name) && source.TryGetValue(name, out var other) && Copy(other, member))
                copied++;
        }

        foreach (var names in Roles)
        {
            var from1 = First(source, names);
            var to1 = First(target, names);
            if (from1 == null || to1 == null || from1.Value.Name == to1.Value.Name)
                continue;
            if (Copy(from1.Value.Member, to1.Value.Member))
                copied++;
        }
        return copied;
    }

    private static Dictionary<string, Member> Members(MaterialProvider material)
    {
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);
        for (int i = 0; i < material.SyncMemberCount; i++)
        {
            if (material.GetSyncMember(i) is not IField field)
                continue;
            members[material.GetSyncMemberName(i)] = new Member
            {
                Kind = field.GetType(),
                IsProperty = false,
                ValueType = field.ValueType,
                Get = () => field.BoxedValue,
                Set = field.CanWrite ? value => field.BoxedValue = value! : null,
            };
        }

        foreach (var property in material.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var owner = property.DeclaringType;
            if (owner == null || owner == typeof(MaterialProvider) || !typeof(MaterialProvider).IsAssignableFrom(owner)
                || !property.CanRead || property.SetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0
                || !Carries(property.PropertyType) || members.ContainsKey(property.Name))
                continue;
            members[property.Name] = new Member
            {
                Kind = property.PropertyType,
                IsProperty = true,
                ValueType = property.PropertyType,
                Get = () => property.GetValue(material),
                Set = value => property.SetValue(material, value),
            };
        }
        return members;
    }

    // A value, or an asset the material draws with. Anything else a kind exposes is not part of its look.
    private static bool Carries(Type type) =>
        type.IsValueType || (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAssetProvider<>));

    private static (string Name, Member Member)? First(Dictionary<string, Member> members, string[] names)
    {
        foreach (var name in names)
        {
            if (members.TryGetValue(name, out var member))
                return (name, member);
        }
        return null;
    }

    // The same kind of member copies as it is (a reference only into a reference to the same asset kind:
    // every reference boxes as an id, so the value type alone would let a texture land in a cubemap
    // slot); a plain property meets a field of its own value type; the two colour kinds cross over;
    // anything else is left alone.
    private static bool Copy(Member from, Member to)
    {
        if (to.Set == null)
            return false;
        object? value = from.Get();
        bool sameKind = from.Kind == to.Kind || ((from.IsProperty || to.IsProperty) && from.ValueType == to.ValueType);
        if (!sameKind)
        {
            if (value is color plain && to.ValueType == typeof(colorHDR))
                value = new colorHDR(plain.r, plain.g, plain.b, plain.a);
            else if (value is colorHDR bright && to.ValueType == typeof(color))
                value = new color(bright.r, bright.g, bright.b, bright.a);
            else
                return false;
        }
        if (Equals(to.Get(), value))
            return false;
        to.Set(value);
        return true;
    }
}
