// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets.Poiyomi;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// A Poiyomi .mat onto the ported Poiyomi material, property for property under the same names: every float,
// color, vector, texture and cubemap the file saves that Poiyomi has, with each texture's tiling and offset
// as its _ST. Values equal to Poiyomi's default are left out, the material reads those from the shader anyway.
//
// Two kinds of name do not match as saved. A locked material's separately animated properties carry a
// suffix, and the suffixed value is the one the material showed. An older Poiyomi saved some modules under
// names the current one renamed; those go across under the new name when the file has no value of its own
// there, and only those whose meaning carried over unchanged. -xlinka
public static class PoiyomiMaterialBuilder
{
    // Older name -> current name, for the switches and values of modules that kept their meaning.
    private static readonly (string Old, string New)[] Renamed =
    {
        ("_EnableLighting", "_ShadingEnabled"),
        ("_EnableBRDF", "_MochieBRDF"),
        ("_BRDFMetallic", "_MochieMetallicMultiplier"),
        ("_BRDFGlossiness", "_MochieRoughnessMultiplier"),
        ("_BRDFReflectionsEnabled", "_MochieReflectionStrength"),
        ("_BRDFSpecularEnabled", "_MochieSpecularStrength"),
        ("_BRDFForceFallback", "_MochieForceFallback"),
        ("_BRDFFallback", "_MochieReflCube"),
        ("_EnableClearCoat", "_ClearCoatBRDF"),
        ("_Clearcoat", "_ClearCoatStrength"),
        ("_ClearcoatGlossiness", "_ClearCoatSmoothness"),
        ("_ClearcoatEnableReflections", "_ClearCoatReflectionStrength"),
        ("_ClearcoatEnableSpecular", "_ClearCoatSpecularStrength"),
        ("_ClearcoatForceFallback", "_ClearCoatForceFallback"),
        ("_ClearcoatFallback", "_ClearCoatFallback"),
    };

    // What the file says, under the names the port knows. Unknown names are kept under their own name and
    // counted by the builder.
    private sealed class Effective
    {
        public readonly Dictionary<string, float> Floats = new(StringComparer.Ordinal);
        public readonly Dictionary<string, color> Colors = new(StringComparer.Ordinal);
        public readonly Dictionary<string, UnityTextureSlot> Textures = new(StringComparer.Ordinal);
        public Dictionary<string, string> Renames = null!;
    }

    private static Effective Read(UnityMaterialData source)
    {
        var e = new Effective { Renames = PoiyomiLockedNames.Renames(source) };
        foreach (var (name, value) in source.Floats)
            e.Floats[name] = value;
        foreach (var (name, value) in source.Colors)
            e.Colors[name] = value;
        foreach (var (name, value) in source.Textures)
            e.Textures[name] = value;
        foreach (var (renamed, original) in e.Renames)
        {
            if (e.Floats.Remove(renamed, out var f))
                e.Floats[original] = f;
            if (e.Colors.Remove(renamed, out var c))
                e.Colors[original] = c;
            if (e.Textures.Remove(renamed, out var t))
                e.Textures[original] = t;
        }
        foreach (var (old, now) in Renamed)
        {
            if (source.HasProperty(now) || PoiyomiProperties.IsKnown(old))
                continue;
            if (e.Floats.Remove(old, out var f))
                e.Floats[now] = f;
            if (e.Textures.Remove(old, out var t))
                e.Textures[now] = t;
        }
        // The old metallic workflow inverted smoothness with a switch of its own.
        if (!source.HasProperty("_MochieRoughnessMultiplier") && source.Float("_BRDFInvertGlossiness") > 0.5f
            && e.Floats.TryGetValue("_MochieRoughnessMultiplier", out var gloss))
            e.Floats["_MochieRoughnessMultiplier"] = 1f - gloss;
        return e;
    }

    // The editor's own bookkeeping saved beside the real properties: which sections were open, labels, hints.
    private static bool IsProperty(string name)
        => name.StartsWith('_') && !name.StartsWith("_commentIfZero", StringComparison.Ordinal);

    public static IEnumerable<(string Guid, bool Normal)> TexturesUsed(UnityMaterialData source)
    {
        foreach (var (name, slot) in Read(source).Textures)
        {
            if (slot.Guid is not { } guid || !PoiyomiProperties.TryGet(name, out var property)
                || property.Type != ShaderUniformType.Texture2D)
                continue;
            yield return (guid, property.NormalMap);
        }
    }

    public static IEnumerable<string> CubemapsUsed(UnityMaterialData source)
    {
        foreach (var (name, slot) in Read(source).Textures)
            if (slot.Guid is { } guid && PoiyomiProperties.TryGet(name, out var property) && property.Type == ShaderUniformType.TextureCube)
                yield return guid;
    }

    public static PoiyomiMaterial Build(Slot slot, UnityMaterialData source,
        Func<UnityTextureSlot?, bool, ImageProvider?> texture, Func<string, StaticCubemap?>? cubemap = null)
    {
        var material = slot.AttachComponent<PoiyomiMaterial>();
        var effective = Read(source);
        PoiyomiLockedNames.Remember(material, effective.Renames);
        var unknown = new List<string>();
        var missingCubes = new List<string>();

        foreach (var (name, value) in effective.Floats)
        {
            if (!PoiyomiProperties.TryGet(name, out var property))
            {
                if (IsProperty(name))
                    unknown.Add(name);
                continue;
            }
            if (value != property.Default.x)
                material.SetFloat(name, value);
        }

        foreach (var (name, value) in effective.Colors)
        {
            if (!PoiyomiProperties.TryGet(name, out var property))
            {
                if (IsProperty(name))
                    unknown.Add(name);
                continue;
            }
            var v = new float4(value.r, value.g, value.b, value.a);
            if (v != property.Default)
                material.SetVector(name, v);
        }

        foreach (var (name, slotInfo) in effective.Textures)
        {
            if (!PoiyomiProperties.TryGet(name, out var property))
            {
                if (slotInfo.Guid != null)
                    unknown.Add(name);
                continue;
            }
            if (slotInfo.Guid == null)
                continue;
            if (property.Type == ShaderUniformType.TextureCube)
            {
                if (cubemap?.Invoke(slotInfo.Guid) is { } cube)
                    material.SetCubemap(name, cube);
                else
                    missingCubes.Add(name);
                continue;
            }
            if (property.Type != ShaderUniformType.Texture2D)
                continue;
            if (texture(slotInfo, false) is { } image)
                material.SetTexture(name, image);
            var st = new float4(slotInfo.Scale.x, slotInfo.Scale.y, slotInfo.Offset.x, slotInfo.Offset.y);
            if (PoiyomiProperties.TryGet(name + "_ST", out var stProperty) && st != stProperty.Default)
                material.SetVector(name + "_ST", st);
        }

        material.RenderQueue.Value = source.RenderQueue;

        if (missingCubes.Count > 0)
            Logger.Debug($"Poiyomi material '{source.Name}': cubemaps whose picture is not in the package ({string.Join(", ", missingCubes)}).");
        if (unknown.Count > 0)
            Logger.Debug($"Poiyomi material '{source.Name}': {unknown.Count} saved properties this Poiyomi version does not have.");
        return material;
    }
}
