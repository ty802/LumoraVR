// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Lumora.Core.Assets.Poiyomi;

namespace Lumora.Core.Assets.Interop.Unity;

// Locking a Poiyomi material renames every property marked to animate on its own: the tag "<name>Animated"
// reads 2, and the locked shader's uniform becomes "<name>_<suffix>", the suffix from the material's
// "thry_rename_suffix" tag or else its name stripped to letters, digits and underscores. Clips and toggle
// actions name the renamed one, the .mat saves both, and the port only knows the original. This answers
// "which property is that" per converted material. -xlinka
public static class PoiyomiLockedNames
{
    private static readonly ConditionalWeakTable<MaterialProvider, Dictionary<string, string>> _byMaterial = new();
    private static readonly Regex NotSuffix = new("[^0-9a-zA-Z_]", RegexOptions.Compiled);

    // Renamed name -> original, for one .mat. Empty for a material that was never locked with renames.
    public static Dictionary<string, string> Renames(UnityMaterialData data)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        string? suffix = null;
        foreach (var (tag, value) in data.Tags)
        {
            if (value != "2" || !tag.EndsWith("Animated", StringComparison.Ordinal) || tag.Length <= "Animated".Length)
                continue;
            suffix ??= Suffix(data);
            if (suffix.Length == 0)
                break;
            string original = tag[..^"Animated".Length];
            renames[original + "_" + suffix] = original;
        }
        return renames;
    }

    public static string Suffix(UnityMaterialData data)
        => data.Tags.TryGetValue("thry_rename_suffix", out var tagged) && !string.IsNullOrEmpty(tagged)
            ? tagged
            : NotSuffix.Replace(data.Name.Trim(), "");

    // The original name of a property a clip animates on this material: the renamed name when the material
    // was locked with it, read back through what its .mat said; the name as given otherwise. A name the tags
    // do not list, or one on a converted material the importer never saw (a copy made later, one loaded from
    // a save), is read by shape: the longest leading part before an underscore that Poiyomi knows.
    public static string Original(MaterialProvider? material, string property)
    {
        if (material != null && _byMaterial.TryGetValue(material, out var renames) && renames.TryGetValue(property, out var original))
            return original;
        if (material is not PoiyomiMaterial || PoiyomiProperties.IsKnown(property))
            return property;
        for (int at = property.LastIndexOf('_'); at > 1; at = property.LastIndexOf('_', at - 1))
        {
            var head = property[..at];
            if (PoiyomiProperties.IsKnown(head))
                return head;
        }
        return property;
    }

    internal static void Remember(MaterialProvider material, Dictionary<string, string> renames)
    {
        if (renames.Count > 0)
            _byMaterial.AddOrUpdate(material, renames);
    }

    // A material copied for one renderer answers the same as the one it was copied from.
    internal static void Share(MaterialProvider copy, MaterialProvider source)
    {
        if (_byMaterial.TryGetValue(source, out var renames))
            _byMaterial.AddOrUpdate(copy, renames);
    }
}
