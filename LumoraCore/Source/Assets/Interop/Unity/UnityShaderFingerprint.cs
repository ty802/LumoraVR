// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Lumora.Core.Assets.Interop.Unity;

// A shader the package leaves out, known by what its materials have in common. A material saves every
// property of the shader it is on and keeps those of every shader it had before, so across all the
// materials on one shader the shared set is that shader's own, and one material's leftovers drop out.
// One material alone says nothing beyond itself; that takes two. -xlinka
public static class UnityShaderFingerprint
{
    private static readonly Regex ShaderGuid = new(@"guid:\s*'?([0-9a-fA-F]{32})", RegexOptions.Compiled);

    private sealed class Cache
    {
        public Dictionary<string, List<UnityPackageAsset>>? ByShader;
        public readonly Dictionary<string, UnityMaterialData?> Common = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<UnityPackageArchive, Cache> _caches = new();

    public static UnityMaterialData? Common(UnityPackageArchive archive, string shaderGuid)
    {
        var cache = _caches.GetValue(archive, _ => new Cache());
        lock (cache)
        {
            if (cache.Common.TryGetValue(shaderGuid, out var known))
                return known;
            cache.ByShader ??= Group(archive);
            UnityMaterialData? common = null;
            if (cache.ByShader.TryGetValue(shaderGuid, out var mats) && mats.Count >= 2)
            {
                var all = new List<UnityMaterialData>();
                foreach (var asset in mats)
                    if (UnityMaterialData.Parse(archive.ReadText(asset)) is { } data)
                        all.Add(data);
                if (all.Count >= 2)
                    common = Intersect(all);
            }
            cache.Common[shaderGuid] = common;
            return common;
        }
    }

    private static Dictionary<string, List<UnityPackageAsset>> Group(UnityPackageArchive archive)
    {
        var groups = new Dictionary<string, List<UnityPackageAsset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in archive.OfExtension(".mat"))
        {
            var text = archive.ReadText(asset);
            if (text == null)
                continue;
            int at = text.IndexOf("m_Shader:", StringComparison.Ordinal);
            if (at < 0)
                continue;
            var match = ShaderGuid.Match(text, at, System.Math.Min(240, text.Length - at));
            if (!match.Success)
                continue;
            var guid = match.Groups[1].Value;
            if (!groups.TryGetValue(guid, out var list))
                groups[guid] = list = new List<UnityPackageAsset>();
            list.Add(asset);
        }
        return groups;
    }

    private static UnityMaterialData Intersect(List<UnityMaterialData> all)
    {
        var first = all[0];
        var common = new UnityMaterialData { Name = first.Name, Shader = first.Shader };
        foreach (var (name, value) in first.Floats)
            if (all.TrueForAll(m => m.Floats.ContainsKey(name)))
                common.Floats[name] = value;
        foreach (var (name, value) in first.Colors)
            if (all.TrueForAll(m => m.Colors.ContainsKey(name)))
                common.Colors[name] = value;
        foreach (var (name, value) in first.Textures)
            if (all.TrueForAll(m => m.Textures.ContainsKey(name)))
                common.Textures[name] = value;
        return common;
    }
}
