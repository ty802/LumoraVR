// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

public readonly record struct UnityTextureSlot(string? Guid, float2 Scale, float2 Offset);

// One .mat file: the shader it was made for and every saved property, by name. Properties are read in
// both layouts Unity has written over the years ("- _MainTex:" entries, and the older "first: {name}
// second: value" pairs), so a material from an old project reads the same as a new one. -xlinka
public sealed class UnityMaterialData
{
    public string Name = "";
    public UnityRef Shader;
    public readonly HashSet<string> Keywords = new(StringComparer.Ordinal);
    // The keywords the material's own shader knew when it was saved; the rest are leftovers.
    public readonly HashSet<string> ValidKeywords = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> Tags = new(StringComparer.Ordinal);
    public readonly HashSet<string> DisabledPasses = new(StringComparer.Ordinal);
    public readonly Dictionary<string, UnityTextureSlot> Textures = new(StringComparer.Ordinal);
    public readonly Dictionary<string, float> Floats = new(StringComparer.Ordinal);
    public readonly Dictionary<string, color> Colors = new(StringComparer.Ordinal);
    public int RenderQueue = -1;

    public static UnityMaterialData? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        UnityDocument? doc = null;
        foreach (var d in UnityYaml.Parse(text))
        {
            if (d.ClassId == 21)
            {
                doc = d;
                break;
            }
        }
        if (doc == null)
            return null;

        var mat = new UnityMaterialData
        {
            Name = doc["m_Name"]?.Text ?? "",
            Shader = doc["m_Shader"]?.Ref ?? default,
            RenderQueue = doc["m_CustomRenderQueue"]?.Int(-1) ?? -1,
        };

        foreach (var key in new[] { "m_ShaderKeywords" })
        {
            var words = doc[key]?.Text;
            if (!string.IsNullOrEmpty(words))
                foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    mat.Keywords.Add(w);
                    mat.ValidKeywords.Add(w);
                }
        }
        foreach (var key in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
            if (doc[key] is UnitySeq list)
                foreach (var item in list.Items)
                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        mat.Keywords.Add(item.Text);
                        if (key == "m_ValidKeywords")
                            mat.ValidKeywords.Add(item.Text);
                    }

        if (doc["disabledShaderPasses"] is UnitySeq passes)
            foreach (var item in passes.Items)
                if (!string.IsNullOrEmpty(item.Text))
                    mat.DisabledPasses.Add(item.Text);

        if (doc["stringTagMap"] is UnityMap tags)
            foreach (var pair in tags.Entries)
                if (pair.Value.Text is { } value)
                    mat.Tags[pair.Key] = value;

        var saved = doc["m_SavedProperties"];
        if (saved != null)
        {
            foreach (var (name, value) in Entries(saved["m_TexEnvs"]))
            {
                var tex = value["m_Texture"]?.Ref ?? default;
                mat.Textures[name] = new UnityTextureSlot(
                    string.IsNullOrEmpty(tex.Guid) || tex.FileId == 0 ? null : tex.Guid,
                    Vector2(value["m_Scale"], new float2(1f, 1f)),
                    Vector2(value["m_Offset"], new float2(0f, 0f)));
            }
            foreach (var (name, value) in Entries(saved["m_Floats"]))
                mat.Floats[name] = value.Float();
            foreach (var (name, value) in Entries(saved["m_Ints"]))
                mat.Floats[name] = value.Float();
            foreach (var (name, value) in Entries(saved["m_Colors"]))
                mat.Colors[name] = new color(
                    value["r"]?.Float() ?? 0f, value["g"]?.Float() ?? 0f, value["b"]?.Float() ?? 0f, value["a"]?.Float(1f) ?? 1f);
        }
        return mat;
    }

    // "- _Name: value" (each item a one-entry mapping) or "- first: {name: _Name} second: value".
    private static IEnumerable<(string Name, UnityNode Value)> Entries(UnityNode? list)
    {
        if (list is UnitySeq seq)
        {
            foreach (var item in seq.Items)
            {
                if (item is not UnityMap map || map.Entries.Count == 0)
                    continue;
                if (map["first"] is { } first && map["second"] is { } second)
                {
                    var name = first["name"]?.Text;
                    if (name != null)
                        yield return (name, second);
                    continue;
                }
                yield return (map.Entries[0].Key, map.Entries[0].Value);
            }
        }
        else if (list is UnityMap map)
        {
            foreach (var pair in map.Entries)
                yield return (pair.Key, pair.Value);
        }
    }

    private static float2 Vector2(UnityNode? node, float2 fallback)
        => node == null ? fallback : new float2(node["x"]?.Float(fallback.x) ?? fallback.x, node["y"]?.Float(fallback.y) ?? fallback.y);

    public bool HasProperty(string name)
        => Floats.ContainsKey(name) || Colors.ContainsKey(name) || Textures.ContainsKey(name);

    public float Float(string name, float fallback = 0f) => Floats.TryGetValue(name, out var v) ? v : fallback;

    public bool Toggle(string name) => Floats.TryGetValue(name, out var v) && v > 0.5f;

    public color Color(string name, color fallback) => Colors.TryGetValue(name, out var v) ? v : fallback;

    public UnityTextureSlot? Texture(string name)
        => Textures.TryGetValue(name, out var slot) && slot.Guid != null ? slot : null;

    // The first of several names a property goes by across shader families.
    public UnityTextureSlot? Texture(params string[] names)
    {
        foreach (var name in names)
            if (Texture(name) is { } slot)
                return slot;
        return null;
    }

    public color? FirstColor(params string[] names)
    {
        foreach (var name in names)
            if (Colors.TryGetValue(name, out var v))
                return v;
        return null;
    }

    public float? FirstFloat(params string[] names)
    {
        foreach (var name in names)
            if (Floats.TryGetValue(name, out var v))
                return v;
        return null;
    }
}
