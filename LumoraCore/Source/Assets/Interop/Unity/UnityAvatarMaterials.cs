// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// Material values a clip sets on a renderer ("material._Color.r", "material._MainHueShift"), put onto the
// fields of the materials we converted.
//
// Unity animates a material value per renderer and per channel, so a pose's channels for one renderer
// are gathered and laid over each of its materials' current values. Unity's change touches only that
// renderer; ours are shared assets, so a material the renderer shares with one the clip leaves alone is
// copied for it first. A Poiyomi material keeps Poiyomi's own property names, so its channels land on the
// property of that name as they are. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private enum MaterialValue { Albedo, Emission, EmissionStrength, Rim, Outline, Matcap, Hue, HueSwitch, Saturation, Metallic, Smoothness, Cutoff, OutlineWidth, HueSaturationValue }

        private static MaterialValue? MaterialProperty(string name) => name switch
        {
            "_Color" or "_BaseColor" or "_MainColor" or "_TintColor" => MaterialValue.Albedo,
            "_EmissionColor" or "_EmissiveColor" or "_Emissive_Color" => MaterialValue.Emission,
            "_EmissionStrength" => MaterialValue.EmissionStrength,
            "_RimColor" or "_RimLightColor" => MaterialValue.Rim,
            "_OutlineColor" or "_LineColor" => MaterialValue.Outline,
            "_MatcapColor" or "_MatCapColor" => MaterialValue.Matcap,
            "_MainHueShift" => MaterialValue.Hue,
            "_MainHueShiftToggle" or "_MainColorAdjustToggle" => MaterialValue.HueSwitch,
            "_Saturation" => MaterialValue.Saturation,
            "_Metallic" or "_MochieMetallicMultiplier" => MaterialValue.Metallic,
            "_Glossiness" or "_Smoothness" or "_MochieRoughnessMultiplier" => MaterialValue.Smoothness,
            "_Cutoff" => MaterialValue.Cutoff,
            "_OutlineWidth" or "_LineWidth" => MaterialValue.OutlineWidth,
            "_MainTexHSVG" => MaterialValue.HueSaturationValue,
            _ => null,
        };

        // "material._Color.r" -> ("_Color", 'r'); a single number has no channel.
        private static (string Property, char Channel) MaterialChannel(string attribute)
        {
            var rest = attribute["material.".Length..];
            int dot = rest.LastIndexOf('.');
            if (dot > 0 && dot == rest.Length - 2 && "rgbaxyzw".IndexOf(rest[^1]) >= 0)
                return (rest[..dot], rest[^1]);
            return (rest, '\0');
        }

        // One renderer's material channels in a pose: property -> channel -> value.
        private static Dictionary<string, Dictionary<string, Dictionary<char, float>>> MaterialChannels(UnityPose pose)
        {
            var byPath = new Dictionary<string, Dictionary<string, Dictionary<char, float>>>(StringComparer.Ordinal);
            foreach (var (binding, value) in pose.Floats)
            {
                if (!binding.Attribute.StartsWith("material.", StringComparison.Ordinal))
                    continue;
                var (property, channel) = MaterialChannel(binding.Attribute);
                if (!byPath.TryGetValue(binding.Path, out var properties))
                    byPath[binding.Path] = properties = new Dictionary<string, Dictionary<char, float>>(StringComparer.Ordinal);
                if (!properties.TryGetValue(property, out var channels))
                    properties[property] = channels = new Dictionary<char, float>();
                channels[channel] = value;
            }
            return byPath;
        }

        // What the channels make of one material: each field they change and its new value (colorHDR or
        // float), starting from what the field holds now.
        private Dictionary<IField, object> MaterialFieldValues(MaterialProvider material, Dictionary<string, Dictionary<char, float>> properties)
        {
            var result = new Dictionary<IField, object>();
            if (material is PoiyomiMaterial poiyomi)
            {
                foreach (var (property, channels) in properties)
                {
                    if (poiyomi.Ensure(PoiyomiLockedNames.Original(poiyomi, property)) is not { } param)
                    {
                        Skip("material values with nothing to land on");
                        continue;
                    }
                    var c = result.TryGetValue(param.Value, out var known) ? (float4)known : param.Value.Value;
                    result[param.Value] = channels.TryGetValue('\0', out var single)
                        ? new float4(single, c.y, c.z, c.w)
                        : new float4(Channel(channels, 'r', 'x', c.x), Channel(channels, 'g', 'y', c.y),
                            Channel(channels, 'b', 'z', c.z), Channel(channels, 'a', 'w', c.w));
                }
                return result;
            }
            float? strength = null;
            bool? hueSwitch = null;
            float? hue = null, saturation = null;
            foreach (var (property, channels) in properties)
            {
                var kind = MaterialProperty(property);
                if (kind == null)
                {
                    Skip("material values with nothing to land on");
                    continue;
                }
                float Single() => channels.TryGetValue('\0', out var v) ? v : channels.Values.First();
                switch (kind.Value)
                {
                    case MaterialValue.EmissionStrength:
                        strength = Single();
                        break;
                    case MaterialValue.HueSwitch:
                        hueSwitch = (hueSwitch ?? true) && Single() > 0.5f;
                        break;
                    case MaterialValue.Hue:
                        hue = Single();
                        break;
                    case MaterialValue.Saturation:
                        saturation = System.Math.Clamp(Single(), -1f, 1f);
                        break;
                    case MaterialValue.HueSaturationValue:
                        if (channels.TryGetValue('r', out var h) || channels.TryGetValue('x', out h))
                            hue = h;
                        if (channels.TryGetValue('g', out var s) || channels.TryGetValue('y', out s))
                            saturation = System.Math.Clamp(s - 1f, -1f, 1f);
                        break;
                    default:
                        if (ColorField(material, kind.Value) is { } color)
                        {
                            var c = result.TryGetValue(color, out var known) ? (colorHDR)known : color.Value;
                            result[color] = new colorHDR(
                                Channel(channels, 'r', 'x', c.r), Channel(channels, 'g', 'y', c.g),
                                Channel(channels, 'b', 'z', c.b), Channel(channels, 'a', 'w', c.a));
                        }
                        else if (FloatField(material, kind.Value) is { } number)
                        {
                            float v = Single();
                            result[number] = kind.Value == MaterialValue.OutlineWidth ? v * 0.01f : v;
                        }
                        else
                        {
                            Skip("material values this material has no place for");
                        }
                        break;
                }
            }
            if (strength is { } k && ColorField(material, MaterialValue.Emission) is { } emission)
            {
                var c = result.TryGetValue(emission, out var known) ? (colorHDR)known : emission.Value;
                result[emission] = new colorHDR(c.r * k, c.g * k, c.b * k, c.a);
            }
            if (material is XiexeToonMaterial toon)
            {
                if (hueSwitch == false)
                    result[toon.HueShift] = 0f;
                // Kept as given: a slider runs it from 0 to a whole turn, and the shader wraps it.
                else if (hue is { } turns)
                    result[toon.HueShift] = turns;
                if (saturation is { } amount)
                    result[toon.Saturation] = amount;
            }
            return result;
        }

        private static float Channel(Dictionary<char, float> channels, char a, char b, float current)
            => channels.TryGetValue(a, out var v) || channels.TryGetValue(b, out v) ? v : current;

        private static Sync<colorHDR>? ColorField(MaterialProvider material, MaterialValue kind) => (material, kind) switch
        {
            (XiexeToonMaterial t, MaterialValue.Albedo) => t.AlbedoColor,
            (XiexeToonMaterial t, MaterialValue.Emission) => t.EmissionColor,
            (XiexeToonMaterial t, MaterialValue.Rim) => t.RimColor,
            (XiexeToonMaterial t, MaterialValue.Outline) => t.OutlineColor,
            (XiexeToonMaterial t, MaterialValue.Matcap) => t.MatcapTint,
            (PBS_Metallic m, MaterialValue.Albedo) => m.AlbedoColor,
            (PBS_Metallic m, MaterialValue.Emission) => m.EmissiveColor,
            (PBS_Specular s, MaterialValue.Albedo) => s.AlbedoColor,
            (PBS_Specular s, MaterialValue.Emission) => s.EmissiveColor,
            (UnlitMaterial u, MaterialValue.Albedo) => u.TintColor,
            _ => null,
        };

        private static Sync<float>? FloatField(MaterialProvider material, MaterialValue kind) => (material, kind) switch
        {
            (XiexeToonMaterial t, MaterialValue.Metallic) => t.Metallic,
            (XiexeToonMaterial t, MaterialValue.Smoothness) => t.Glossiness,
            (XiexeToonMaterial t, MaterialValue.Cutoff) => t.AlphaClip,
            (XiexeToonMaterial t, MaterialValue.OutlineWidth) => t.OutlineWidth,
            (PBS_Metallic m, MaterialValue.Metallic) => m.Metallic,
            (PBS_Metallic m, MaterialValue.Smoothness) => m.Smoothness,
            (PBS_Metallic m, MaterialValue.Cutoff) => m.AlphaCutoff,
            (PBS_Specular s, MaterialValue.Cutoff) => s.AlphaCutoff,
            (UnlitMaterial u, MaterialValue.Cutoff) => u.AlphaCutoff,
            _ => null,
        };

        // A renderer's material slots: its per-surface list, or its single material when it keeps no list.
        private static List<AssetRef<MaterialAsset>> MaterialSlotsOf(Slot slot)
        {
            var list = new List<AssetRef<MaterialAsset>>();
            if (slot.GetComponent<SkinnedMeshRenderer>() is { } skinned)
            {
                if (skinned.Materials.Count > 0)
                    list.AddRange((IList<AssetRef<MaterialAsset>>)skinned.Materials);
                else
                    list.Add(skinned.Material);
            }
            else if (slot.GetComponent<MeshRenderer>() is { } mesh)
            {
                list.AddRange((IList<AssetRef<MaterialAsset>>)mesh.Materials);
            }
            return list;
        }

        private readonly Dictionary<(MaterialProvider, Slot), MaterialProvider> _ownMaterials = new();

        // The materials of the renderer at this path, each its own: one it shares with a renderer the
        // change does not reach is copied, and the renderer is pointed at the copy.
        private List<MaterialProvider> OwnMaterials(string path)
            => _nodes.AtPath(path) is { } slot ? OwnMaterials(slot) : new List<MaterialProvider>();

        private List<MaterialProvider> OwnMaterials(Slot slot)
        {
            var list = new List<MaterialProvider>();
            var slots = MaterialSlotsOf(slot);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Target is not MaterialProvider material)
                    continue;
                if (!_ownMaterials.TryGetValue((material, slot), out var own))
                {
                    own = material;
                    if (!slots[i].IsDriven && SharedOutside(material, slot))
                    {
                        own = (MaterialProvider)material.Slot.AttachComponent(material.GetType());
                        own.CopyValues(material);
                        PoiyomiLockedNames.Share(own, material);
                    }
                    _ownMaterials[(material, slot)] = own;
                }
                if (!ReferenceEquals(own, material) && !slots[i].IsDriven)
                    slots[i].Target = own;
                if (!list.Contains(own))
                    list.Add(own);
            }
            return list;
        }

        private bool SharedOutside(MaterialProvider material, Slot owner)
        {
            foreach (var renderer in _model.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (!ReferenceEquals(renderer.Slot, owner) && MaterialSlotsOf(renderer.Slot).Any(m => ReferenceEquals(m.Target, material)))
                    return true;
            foreach (var renderer in _model.GetComponentsInChildren<MeshRenderer>())
                if (!ReferenceEquals(renderer.Slot, owner) && MaterialSlotsOf(renderer.Slot).Any(m => ReferenceEquals(m.Target, material)))
                    return true;
            return false;
        }

        // A switch's material values: per field, its on and off values, held by a value driver.
        private void MaterialValues(UnityPose on, UnityPose off, IField<bool> sw, Slot host)
        {
            var onPaths = MaterialChannels(on);
            var offPaths = MaterialChannels(off);
            foreach (var path in onPaths.Keys.Union(offPaths.Keys).ToList())
            {
                foreach (var material in OwnMaterials(path))
                {
                    var onValues = onPaths.TryGetValue(path, out var a) ? MaterialFieldValues(material, a) : new();
                    var offValues = offPaths.TryGetValue(path, out var b) ? MaterialFieldValues(material, b) : new();
                    foreach (var field in onValues.Keys.Union(offValues.Keys).ToList())
                    {
                        switch (field)
                        {
                            case Sync<colorHDR> color:
                                Switch(color, offValues.TryGetValue(field, out var cOff) ? (colorHDR)cOff : color.Value,
                                    onValues.TryGetValue(field, out var cOn) ? (colorHDR)cOn : color.Value, sw, host);
                                break;
                            case Sync<float> number:
                                Switch(number, offValues.TryGetValue(field, out var fOff) ? (float)fOff : number.Value,
                                    onValues.TryGetValue(field, out var fOn) ? (float)fOn : number.Value, sw, host);
                                break;
                            case Sync<bool> flag:
                                Switch(flag, offValues.TryGetValue(field, out var bOff) ? (bool)bOff : flag.Value,
                                    onValues.TryGetValue(field, out var bOn) ? (bool)bOn : flag.Value, sw, host);
                                break;
                        }
                    }
                }
            }
        }

        // A VRCFury material property action: one value (or colour, or vector) on the renderer it names, or
        // on every renderer. Scale is how far along a slider toggle's range it sits, from the material's
        // own value to the action's.
        private void MaterialProperty(UnityNode data, IField<bool> sw, float scale, Slot host)
        {
            var property = data["propertyName"]?.Text;
            if (string.IsNullOrEmpty(property))
                return;
            var channels = new Dictionary<char, float>();
            switch (data["propertyType"]?.Int() ?? 0)
            {
                case 1:
                {
                    var c = data["valueColor"];
                    channels['r'] = c?["r"]?.Float() ?? 0f;
                    channels['g'] = c?["g"]?.Float() ?? 0f;
                    channels['b'] = c?["b"]?.Float() ?? 0f;
                    channels['a'] = c?["a"]?.Float(1f) ?? 1f;
                    break;
                }
                case 2:
                case 3:
                {
                    var v = data["valueVector"];
                    channels['x'] = v?["x"]?.Float() ?? 0f;
                    channels['y'] = v?["y"]?.Float() ?? 0f;
                    channels['z'] = v?["z"]?.Float() ?? 0f;
                    channels['w'] = v?["w"]?.Float() ?? 0f;
                    break;
                }
                default:
                    channels['\0'] = data["value"]?.Float() ?? 0f;
                    break;
            }
            var properties = new Dictionary<string, Dictionary<char, float>>(StringComparer.Ordinal) { [property] = channels };

            var slots = new List<Slot>();
            if ((data["affectAllMeshes"]?.Int() ?? 0) != 0)
            {
                slots.AddRange(_model.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r => r.Slot));
                slots.AddRange(_model.GetComponentsInChildren<MeshRenderer>().Select(r => r.Slot));
            }
            else if ((Object(data["renderer2"]) ?? Component(data["renderer"])) is { } one)
            {
                slots.Add(one);
            }
            if (slots.Count == 0)
            {
                Skip("material values on renderers not in the package");
                return;
            }
            foreach (var slot in slots.Distinct())
            {
                foreach (var material in OwnMaterials(slot))
                {
                    foreach (var (field, value) in MaterialFieldValues(material, properties))
                    {
                        switch (field)
                        {
                            case Sync<colorHDR> color:
                            {
                                var rest = color.Value;
                                var full = (colorHDR)value;
                                Switch(color, rest, new colorHDR(rest.r + (full.r - rest.r) * scale, rest.g + (full.g - rest.g) * scale,
                                    rest.b + (full.b - rest.b) * scale, rest.a + (full.a - rest.a) * scale), sw, host);
                                break;
                            }
                            case Sync<float> number:
                            {
                                float rest = number.Value;
                                Switch(number, rest, rest + ((float)value - rest) * scale, sw, host);
                                break;
                            }
                            case Sync<bool> flag:
                                Switch(flag, flag.Value, scale >= 0.5f ? (bool)value : flag.Value, sw, host);
                                break;
                        }
                    }
                }
            }
        }

        // The rest pose's material values, written straight in.
        private void RestMaterialValues(UnityPose pose)
        {
            foreach (var (path, properties) in MaterialChannels(pose))
            {
                foreach (var material in OwnMaterials(path))
                {
                    foreach (var (field, value) in MaterialFieldValues(material, properties))
                    {
                        switch (field)
                        {
                            case Sync<colorHDR> color: color.Value = (colorHDR)value; break;
                            case Sync<float> number: number.Value = (float)value; break;
                            case Sync<bool> flag: flag.Value = (bool)value; break;
                        }
                    }
                }
            }
        }

        // A moving clip's material channels as tracks: per material field, a key per sample with every
        // channel the clip moves on that renderer laid over the field's value.
        private void MaterialMotion(UnityAnimationClip clip, AnimationClip ours, List<IField> targets, int frames, Func<int, float> time)
        {
            var curves = clip.Floats.Where(c => c.Attribute.StartsWith("material.", StringComparison.Ordinal)).GroupBy(c => c.Path);
            foreach (var group in curves)
            {
                var materials = OwnMaterials(group.Key);
                foreach (var material in materials)
                {
                    var tracks = new Dictionary<IField, object>();
                    for (int i = 0; i <= frames; i++)
                    {
                        float t = time(i);
                        var properties = new Dictionary<string, Dictionary<char, float>>(StringComparer.Ordinal);
                        foreach (var curve in group)
                        {
                            var (property, channel) = MaterialChannel(curve.Attribute);
                            if (!properties.TryGetValue(property, out var channels))
                                properties[property] = channels = new Dictionary<char, float>();
                            channels[channel] = curve.Sample(t);
                        }
                        foreach (var (field, value) in MaterialFieldValues(material, properties))
                        {
                            if (!tracks.TryGetValue(field, out var track))
                            {
                                var label = "Material." + tracks.Count;
                                track = value is colorHDR ? ours.AddCurveTrack<colorHDR>(group.Key, label)
                                    : value is bool ? ours.AddCurveTrack<bool>(group.Key, label)
                                    : ours.AddCurveTrack<float>(group.Key, label);
                                tracks[field] = track;
                                targets.Add(field);
                            }
                            if (track is CurveAnimationTrack<colorHDR> colors)
                                colors.AddKey(t, (colorHDR)value);
                            else if (track is CurveAnimationTrack<float> numbers)
                                numbers.AddKey(t, (float)value);
                            else if (track is CurveAnimationTrack<bool> flags)
                                flags.AddKey(t, (bool)value);
                        }
                    }
                }
            }
        }
    }
}
