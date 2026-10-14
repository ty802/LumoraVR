// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Utility;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// Menu sliders: a radial menu control, or a toggle set up as a slider, becomes one AvatarSlider in the
// avatar's menu, turned on a dial, and whatever its range moves follows the value.
//
// A radial's pose is read at points along the range. Each thing it changes gets a curve through those
// points, thinned to the ones a straight run between neighbours does not already give: shapes go through a
// BlendShapeCurveDriver, so gestures and blinks keep their place above them, and fields through a
// ValueCurveDriver. A material put in partway along is a material switch thrown by the band of the range
// where it is in. Dials that move the same part share it: the first drives it, the others add their own
// move, size or turn from rest on top. Anything else two dials both set (a weight, a colour, a switch) goes
// to the first, since adding two settings of one weight is no setting at all. With the FX graph played
// live, the slider only feeds its parameter. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private const int SliderSteps = 32;
        private static readonly float[] Ends = { 0f, 1f };
        private static readonly float[] Half = { 0f, 0.5f };

        private readonly Dictionary<string, AvatarSlider> _sliderByParameter = new(StringComparer.Ordinal);
        private readonly Dictionary<AvatarSlider, IField<bool>> _bands = new();
        // The curve driving each field a dial moves, and the field's rest before it.
        private readonly Dictionary<IWorldElement, (Component Driver, object Before)> _curves = new();

        public void Sliders(UnityVrcPlan? plan, bool live)
        {
            if (plan == null)
                return;
            foreach (var entry in plan.Sliders)
            {
                var slider = NewSlider(entry.Path, entry.Default);
                _sliderByParameter.TryAdd(entry.Parameter, slider);
                if (live)
                    continue;
                int before = _carried;
                Curves(entry.Samples, slider);
                if (_carried != before || entry.Body || KeptSliders.Contains(entry.Parameter))
                    continue;
                _empty.Add(entry.Path);
                slider.Slot.Destroy();
                _result.Sliders--;
            }
        }

        // Every animator input for a slider's parameter reads the slider, unless something feeds it already.
        public void SliderInputs()
        {
            if (_sliderByParameter.Count == 0)
                return;
            foreach (var input in _model.GetComponentsInChildren<ImportedAnimatorParameter>())
            {
                if (input.Source.Target == null && _sliderByParameter.TryGetValue(input.Parameter.Value, out var slider) && !slider.IsDestroyed)
                    input.Source.Target = slider.Value;
            }
        }

        private AvatarSlider NewSlider(string path, float value)
        {
            var slot = MenuSlot().AddSlot(string.IsNullOrEmpty(path) ? "Slider" : path);
            var slider = slot.AttachComponent<AvatarSlider>();
            slider.Path.Value = path;
            slider.Default.Value = value;
            slider.Value.Value = value;
            _result.Sliders++;
            return slider;
        }

        // ---- Slider toggles ----

        // The toggle's actions played by how far along the slider is, from rest at nought to the actions
        // themselves at the top. Values run there in a straight line; what can only be on or off (objects,
        // material swaps, world drops) comes on from halfway.
        // A slider that sets a parameter the merged layers read already has its dial from them; the toggle's
        // own actions join it there.
        private void SliderToggle(string path, float initial, List<UnityVrcFury.Entry> actions, string? global = null, string? icon = null)
        {
            var adopted = global != null && _sliderByParameter.TryGetValue(global, out var made) && !made.IsDestroyed ? made : null;
            var slider = adopted ?? NewSlider(path, initial);
            if (icon != null)
                _furyIcons.Add((slider.Icon, icon));
            if (adopted != null)
            {
                adopted.Default.Value = initial;
                adopted.Value.Value = initial;
            }
            if (adopted == null && global != null && AnimatorInput(global) != null)
            {
                _sliderByParameter.TryAdd(global, slider);
                adopted = slider;
            }
            int before = _carried;
            foreach (var action in actions)
                SliderAction(action, slider);
            if (_carried != before || adopted != null)
                return;
            _empty.Add(path);
            slider.Slot.Destroy();
            _result.Sliders--;
        }

        private void SliderAction(UnityVrcFury.Entry action, AvatarSlider slider)
        {
            var data = action.Data;
            var host = slider.Slot;
            IField<float> input = slider.Value;
            switch (action.Class)
            {
                case "ObjectToggleAction":
                case "MaterialAction":
                case "WorldDropAction":
                    Action(action, Band(slider), null, SkinnedMeshRenderer.BlendShapePriorityToggle, 0f, host);
                    return;
                case "ScaleAction":
                {
                    if (Object(data["obj"]) is not { } target)
                    {
                        Skip("actions on objects not in the package");
                        return;
                    }
                    var rest = target.LocalScale.Value;
                    float factor = data["scale"]?.Float(1f) ?? 1f;
                    Curve(target.LocalScale, Ends, new[] { rest, rest * factor }, input, host, Parts);
                    return;
                }
                case "BlendShapeAction":
                {
                    var shape = data["blendShape"]?.Text;
                    if (string.IsNullOrEmpty(shape))
                        return;
                    SkinnedMeshRenderer? renderer = null;
                    if ((data["allRenderers"]?.Int(1) ?? 1) == 0)
                    {
                        renderer = Component(data["renderer"])?.GetComponent<SkinnedMeshRenderer>();
                        if (renderer == null)
                        {
                            Skip("shapes on meshes not in the package");
                            return;
                        }
                    }
                    // From the shape's own weight at nought to the action's at the top: the switch driver's
                    // amount is exactly that blend.
                    var driver = host.AttachComponent<BlendShapeSwitchDriver>();
                    driver.On.Value = true;
                    driver.Amount.Target = input;
                    driver.Renderer.Target = renderer!;
                    driver.Scope.Target = _model;
                    driver.BlendShape.Value = shape;
                    driver.Weight.Value = System.Math.Clamp((data["blendShapeValue"]?.Float(100f) ?? 100f) / 100f, 0f, 1f);
                    driver.Priority.Value = SkinnedMeshRenderer.BlendShapePriorityToggle;
                    _carried++;
                    return;
                }
                case "MaterialPropertyAction":
                    SliderMaterialProperty(data, input, host);
                    return;
                case "AnimationClipAction":
                {
                    var clip = ActionClip(data["clip"]);
                    if (clip == null)
                    {
                        Skip("animation clips not in the package");
                        return;
                    }
                    // Read at how far along the slider is, with rest at nought.
                    var samples = new List<(float At, UnityPose Pose)>();
                    for (int i = 0; i <= SliderSteps; i++)
                    {
                        float at = i / (float)SliderSteps;
                        var pose = new UnityPose();
                        if (i > 0)
                            pose.Add(clip, at * clip.Length, play: false);
                        samples.Add((at, pose));
                    }
                    Curves(samples, slider);
                    return;
                }
                default:
                    Skip(Plain(action.Class));
                    return;
            }
        }

        // A switch that is on from halfway along the slider.
        private IField<bool> Band(AvatarSlider slider)
        {
            if (_bands.TryGetValue(slider, out var band))
                return band;
            var holder = slider.Slot.AttachComponent<ValueHolder<bool>>();
            var curve = slider.Slot.AttachComponent<ValueCurveDriver<bool>>();
            curve.Input.Target = slider.Value;
            curve.AddStop(0f, false);
            curve.AddStop(0.5f, true);
            curve.AddDrive(holder.Value);
            _bands[slider] = holder.Value;
            return holder.Value;
        }

        // One material value (a colour, a number, a vector) on the renderer the action names, or on every
        // renderer, from the material's own value at nought to the action's at the top.
        private void SliderMaterialProperty(UnityNode data, IField<float> input, Slot host)
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
            var properties = new Dictionary<string, Dictionary<char, float>>(StringComparer.Ordinal) { [property] = channels };
            foreach (var slot in slots.Distinct())
                foreach (var material in OwnMaterials(slot))
                    foreach (var (field, value) in MaterialFieldValues(material, properties))
                    {
                        switch (field)
                        {
                            case Sync<colorHDR> color:
                                Curve(color, Ends, new[] { color.Value, (colorHDR)value }, input, host, Parts);
                                break;
                            case Sync<float> number:
                                Curve(number, Ends, new[] { number.Value, (float)value }, input, host, Parts);
                                break;
                            case Sync<float4> vector:
                                Curve(vector, Ends, new[] { vector.Value, (float4)value }, input, host, Parts);
                                break;
                            case Sync<bool> flag:
                                Curve(flag, Half, new[] { flag.Value, (bool)value }, input, host, Parts, step: true);
                                break;
                        }
                    }
        }

        // ---- Sampled poses ----

        // Drivers for everything the samples move, each along its own curve. A binding a sample leaves alone
        // stands at rest there, which is what the avatar holds now: the rest pose was written in first.
        private void Curves(IReadOnlyList<(float At, UnityPose Pose)> samples, AvatarSlider slider)
        {
            if (samples.Count == 0)
                return;
            var host = slider.Slot;
            IField<float> input = slider.Value;
            var at = samples.Select(s => s.At).ToArray();

            foreach (var binding in samples.SelectMany(s => s.Pose.Floats.Keys).Distinct().ToList())
            {
                var column = samples.Select(s => s.Pose.Floats.TryGetValue(binding, out var v) ? v : (float?)null).ToArray();
                SampledFloat(binding, at, column, input, host);
            }

            foreach (var binding in samples.SelectMany(s => s.Pose.Vectors.Keys).Distinct().ToList())
            {
                if (binding.Path.Length == 0)
                {
                    Skip("moves of the avatar's own placement");
                    continue;
                }
                if (_nodes.AtPath(binding.Path) is not { } slot)
                {
                    Skip("changes to objects this avatar does not have");
                    continue;
                }
                switch (binding.Attribute)
                {
                    case "position":
                    {
                        var rest = slot.LocalPosition.Value;
                        Curve(slot.LocalPosition, at, samples.Select(s => s.Pose.Vectors.TryGetValue(binding, out var v) ? Mirror(v) / _unit : rest).ToArray(), input, host, Parts);
                        break;
                    }
                    case "scale":
                    {
                        var rest = slot.LocalScale.Value;
                        Curve(slot.LocalScale, at, samples.Select(s => s.Pose.Vectors.TryGetValue(binding, out var v) ? v : rest).ToArray(), input, host, Parts);
                        break;
                    }
                    case "euler":
                    {
                        var rest = slot.LocalRotation.Value;
                        var turns = Aligned(samples.Select(s => s.Pose.Vectors.TryGetValue(binding, out var v) ? EulerDegrees(v) : rest).ToArray());
                        Curve(slot.LocalRotation, at, turns, input, host, Parts);
                        break;
                    }
                }
            }

            foreach (var binding in samples.SelectMany(s => s.Pose.Rotations.Keys).Distinct().ToList())
            {
                if (binding.Path.Length == 0)
                {
                    Skip("moves of the avatar's own placement");
                    continue;
                }
                if (_nodes.AtPath(binding.Path) is not { } slot)
                {
                    Skip("changes to objects this avatar does not have");
                    continue;
                }
                var rest = slot.LocalRotation.Value;
                var turns = Aligned(samples.Select(s => s.Pose.Rotations.TryGetValue(binding, out var q) ? MirrorRotation(q) : rest).ToArray());
                Curve(slot.LocalRotation, at, turns, input, host, Parts);
            }

            foreach (var binding in samples.SelectMany(s => s.Pose.References.Keys).Distinct().ToList())
            {
                var materialSlot = MaterialSlot(binding);
                if (materialSlot == null)
                {
                    Skip("swaps of things other than materials");
                    continue;
                }
                var rest = materialSlot.Target;
                var column = samples.Select(s => s.Pose.References.TryGetValue(binding, out var r) && r.Guid is { } guid
                    && _materialFor?.Invoke(guid) is { } material ? material : rest).ToArray();
                SwapAlong(materialSlot, rest, at, column, input, host);
            }

            SampledMaterialValues(samples, at, input, host);
        }

        private void SampledFloat(UnityBinding binding, float[] at, float?[] column, IField<float> input, Slot host)
        {
            var kind = Classify(binding);
            if (kind == Target.Muscle)
            {
                Skip("hand and body muscle poses");
                return;
            }
            if (kind == Target.MaterialValue)
                return;
            if (kind == Target.None)
            {
                Skip(binding.ClassId == 114 ? "changes to script settings" : "changes to " + binding.Attribute);
                return;
            }
            var slot = _nodes.AtPath(binding.Path);
            if (slot == null)
            {
                Skip("changes to objects this avatar does not have");
                return;
            }
            switch (kind)
            {
                case Target.Active:
                {
                    bool rest = slot.ActiveSelf.Value;
                    Curve(slot.ActiveSelf, at, column.Select(v => v is { } f ? f > 0.5f : rest).ToArray(), input, host, Parts, step: true);
                    return;
                }
                case Target.Enabled:
                {
                    bool any = false;
                    foreach (var component in EnabledTargets(slot, binding).ToList())
                    {
                        bool rest = component.Enabled.Value;
                        Curve(component.Enabled, at, column.Select(v => v is { } f ? f > 0.5f : rest).ToArray(), input, host, Parts, step: true);
                        any = true;
                    }
                    if (!any)
                        Skip(binding.ClassId == 114 ? "switches of scripts that do not come across" : "switches of other components");
                    return;
                }
                case Target.ConstraintWeight:
                case Target.ConstraintSource:
                {
                    if (ConstraintField(slot, binding, kind) is not { } weight)
                    {
                        Skip("constraint changes on constraints that do not come across");
                        return;
                    }
                    float rest = weight.Value;
                    Curve(weight, at, column.Select(v => v ?? rest).ToArray(), input, host, Parts);
                    return;
                }
                case Target.BlendShape:
                {
                    if (slot.GetComponent<SkinnedMeshRenderer>() is not { } renderer)
                    {
                        Skip("shapes on meshes not in the package");
                        return;
                    }
                    float rest = _restPose.Floats.TryGetValue(binding, out var r) ? r : 0f;
                    var weights = column.Select(v => System.Math.Clamp((v ?? rest) / 100f, 0f, 1f)).ToArray();
                    var parts = weights.Select(w => new[] { w }).ToArray();
                    if (parts.All(p => Same(p, parts[0])))
                        return;
                    var driver = host.AttachComponent<BlendShapeCurveDriver>();
                    driver.Input.Target = input;
                    driver.Renderer.Target = renderer;
                    driver.Scope.Target = _model;
                    driver.BlendShape.Value = binding.Attribute["blendShape.".Length..];
                    driver.Priority.Value = SkinnedMeshRenderer.BlendShapePriorityToggle;
                    foreach (int i in Keep(at, parts, step: false))
                        driver.AddStop(at[i], weights[i]);
                    _carried++;
                    return;
                }
            }
        }

        // Material values along the range: per field of each material the renderer has, its value at every
        // point, laid over what the field holds now.
        private void SampledMaterialValues(IReadOnlyList<(float At, UnityPose Pose)> samples, float[] at, IField<float> input, Slot host)
        {
            var perSample = samples.Select(s => MaterialChannels(s.Pose)).ToList();
            var paths = perSample.SelectMany(p => p.Keys).Distinct(StringComparer.Ordinal).ToList();
            if (paths.Count == 0)
                return;
            // The same property read at every point would be counted as left out once per point.
            var counted = new Dictionary<string, int>(_skipped);
            foreach (var path in paths)
            {
                foreach (var material in OwnMaterials(path))
                {
                    var fields = new Dictionary<IField, object?[]>();
                    for (int i = 0; i < samples.Count; i++)
                    {
                        if (!perSample[i].TryGetValue(path, out var properties))
                            continue;
                        foreach (var (field, value) in MaterialFieldValues(material, properties))
                        {
                            if (!fields.TryGetValue(field, out var column))
                                fields[field] = column = new object?[samples.Count];
                            column[i] = value;
                        }
                    }
                    foreach (var (field, column) in fields)
                    {
                        switch (field)
                        {
                            case Sync<colorHDR> color:
                                Curve(color, at, column.Select(v => v is colorHDR c ? c : color.Value).ToArray(), input, host, Parts);
                                break;
                            case Sync<float> number:
                                Curve(number, at, column.Select(v => v is float f ? f : number.Value).ToArray(), input, host, Parts);
                                break;
                            case Sync<float4> vector:
                                Curve(vector, at, column.Select(v => v is float4 f ? f : vector.Value).ToArray(), input, host, Parts);
                                break;
                            case Sync<bool> flag:
                                Curve(flag, at, column.Select(v => v is bool b ? b : flag.Value).ToArray(), input, host, Parts, step: true);
                                break;
                        }
                    }
                }
            }
            foreach (var key in _skipped.Keys.ToList())
            {
                int was = counted.GetValueOrDefault(key);
                if (_skipped[key] > was + 1)
                    _skipped[key] = was + 1;
            }
        }

        // A material slot that changes partway along: a switch per material it takes, each thrown by the
        // band of the range where that material is in.
        private void SwapAlong(AssetRef<MaterialAsset> materialSlot, IAssetProvider<MaterialAsset>? rest, float[] at,
            IAssetProvider<MaterialAsset>?[] column, IField<float> input, Slot host)
        {
            var taken = column.Where(m => m != null && !ReferenceEquals(m, rest)).Distinct().ToList();
            if (taken.Count == 0)
                return;
            var key = ((IWorldElement)materialSlot, "material");
            if (materialSlot.IsDriven && !(_switches.TryGetValue(key, out var existing) && existing is MaterialSwitchDriver))
            {
                Skip("material swaps on a slot something else drives");
                return;
            }
            if (!(_switches.TryGetValue(key, out var known) && known is MaterialSwitchDriver swap))
            {
                swap = host.AttachComponent<MaterialSwitchDriver>();
                swap.OffMaterial.Target = rest!;
                swap.OnMaterial.Target = taken[0]!;
                swap.AddSlot(materialSlot);
                _switches[key] = swap;
            }
            foreach (var material in taken)
            {
                var flags = column.Select(m => ReferenceEquals(m, material)).ToArray();
                var holder = host.AttachComponent<ValueHolder<bool>>();
                var curve = host.AttachComponent<ValueCurveDriver<bool>>();
                curve.Input.Target = input;
                foreach (int i in Keep(at, flags.Select(f => Parts(f)).ToArray(), step: true))
                    curve.AddStop(at[i], flags[i]);
                curve.AddDrive(holder.Value);
                swap.AddSwitch(holder.Value, material);
            }
            _carried++;
        }

        // ---- Curves ----

        // A field following the slider through these values, thinned. Nothing when they never move. A part's
        // place, size or turn another dial already moves gets this one's change from rest as an offset on
        // that dial's driver; positions and sizes are the only float3 fields and turns the only floatQ ones here.
        private void Curve<T>(Sync<T> field, float[] at, T[] values, IField<float> input, Slot host, Func<T, float[]> parts, bool step = false)
        {
            var columns = values.Select(parts).ToArray();
            if (columns.All(c => Same(c, columns[0])))
                return;
            bool part = typeof(T) == typeof(float3) || typeof(T) == typeof(floatQ);
            if (part && _curves.TryGetValue(field, out var known) && known.Driver is ValueCurveDriver<T> { IsDestroyed: false } first
                && ValueOps<T>.Difference is { } difference && ValueOps<T>.Step != null)
            {
                var rest = (T)known.Before;
                var share = values.Select(v => difference(v, rest)).ToArray();
                var holder = host.AttachComponent<ValueHolder<T>>();
                var offset = host.AttachComponent<ValueCurveDriver<T>>();
                offset.Input.Target = input;
                foreach (int i in Keep(at, share.Select(parts).ToArray(), step: false))
                    offset.AddStop(at[i], share[i]);
                offset.AddDrive(holder.Value);
                first.Offsets.Add(holder.Value);
                _carried++;
                return;
            }
            if (field.IsDriven)
            {
                Skip("changes to values something else drives");
                return;
            }
            var driver = host.AttachComponent<ValueCurveDriver<T>>();
            driver.Input.Target = input;
            foreach (int i in Keep(at, columns, step))
                driver.AddStop(at[i], values[i]);
            driver.AddDrive(field);
            _curves[field] = (driver, field.Value!);
            _carried++;
        }

        private static float[] Parts(bool v) => new[] { v ? 1f : 0f };
        private static float[] Parts(float v) => new[] { v };
        private static float[] Parts(float3 v) => new[] { v.x, v.y, v.z };
        private static float[] Parts(float4 v) => new[] { v.x, v.y, v.z, v.w };
        private static float[] Parts(floatQ v) => new[] { v.x, v.y, v.z, v.w };
        private static float[] Parts(colorHDR v) => new[] { v.r, v.g, v.b, v.a };

        // Each turn on the same side as the one before it, so a curve between them never goes the long way.
        private static floatQ[] Aligned(floatQ[] turns)
        {
            for (int i = 1; i < turns.Length; i++)
            {
                var a = turns[i - 1];
                var b = turns[i];
                if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f)
                    turns[i] = new floatQ(-b.x, -b.y, -b.z, -b.w);
            }
            return turns;
        }

        private static bool Same(float[] a, float[] b)
        {
            for (int k = 0; k < a.Length; k++)
            {
                if (MathF.Abs(a[k] - b[k]) > 1e-5f + 1e-5f * MathF.Abs(a[k]))
                    return false;
            }
            return true;
        }

        // The points a curve needs. A stepped one keeps where the value changes; a blended one drops each
        // point that the straight run between the points either side already passes through, within a
        // five-hundredth of how far that part of the value moves at all.
        private static List<int> Keep(float[] at, float[][] parts, bool step)
        {
            var keep = new List<int> { 0 };
            int n = at.Length;
            if (n <= 1)
                return keep;
            if (step)
            {
                for (int i = 1; i < n; i++)
                    if (!Same(parts[i], parts[i - 1]))
                        keep.Add(i);
                return keep;
            }
            int width = parts[0].Length;
            var tolerance = new float[width];
            for (int k = 0; k < width; k++)
            {
                float low = float.MaxValue, high = float.MinValue;
                foreach (var p in parts)
                {
                    low = MathF.Min(low, p[k]);
                    high = MathF.Max(high, p[k]);
                }
                tolerance[k] = MathF.Max(1e-5f, (high - low) * 0.002f);
            }
            int last = 0;
            for (int i = 1; i < n - 1; i++)
            {
                bool straight = true;
                for (int j = last + 1; j <= i && straight; j++)
                    straight = OnLine(at, parts, last, i + 1, j, tolerance);
                if (straight)
                    continue;
                keep.Add(i);
                last = i;
            }
            keep.Add(n - 1);
            return keep;
        }

        private static bool OnLine(float[] at, float[][] parts, int from, int to, int point, float[] tolerance)
        {
            float span = at[to] - at[from];
            float t = span > 0f ? (at[point] - at[from]) / span : 0f;
            for (int k = 0; k < tolerance.Length; k++)
            {
                float expected = parts[from][k] + (parts[to][k] - parts[from][k]) * t;
                if (MathF.Abs(parts[point][k] - expected) > tolerance[k])
                    return false;
            }
            return true;
        }
    }
}
