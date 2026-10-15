// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Utility;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// Poses from Unity clips put onto the avatar: the pose it rests in is written straight in, and a switch's
// on and off poses become drivers on what they change. A clip that really moves becomes one of our clips
// and plays while its switch is on.
//
// What a pose can change here: objects on and off, renderers and bone chains on and off, blendshapes,
// positions, rotations and sizes, and which material sits in a renderer's slot. Material values (a colour,
// an emission strength) and Unity's muscle-space body poses have nothing to land on and are counted
// instead. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private Slot? _rest;
        private UnityPose _restPose = new();
        private readonly Dictionary<string, AvatarToggle> _toggleByParameter = new(StringComparer.Ordinal);

        public void Menu(UnityVrcPlan? plan)
        {
            if (plan == null)
                return;
            _restPose = plan.Rest;
            ApplyRest(plan.Rest);

            var groups = new Dictionary<string, (List<AvatarToggle> Members, bool Carried, string Folder)>(StringComparer.Ordinal);
            foreach (var entry in plan.Switches)
            {
                var toggle = NewToggle(entry.Path, entry.DefaultOn, entry.Group);
                toggle.Momentary.Value = entry.Momentary;
                toggle.Default.Value = entry.DefaultOn;
                toggle.Saved.Value = entry.Saved;
                Icon(plan, toggle.Icon, entry.Icon);
                if (entry.Group.Length == 0 && entry.Parameter.Length > 0)
                    _toggleByParameter.TryAdd(entry.Parameter, toggle);
                int before = _carried;
                ApplyPose(entry.On, entry.Off, toggle.State, null, SkinnedMeshRenderer.BlendShapePriorityToggle, 0f, toggle.Slot);
                bool carried = _carried != before || entry.Gate;
                if (entry.Group.Length > 0)
                {
                    // A choice that changes nothing still matters inside its group: it is the one that puts
                    // the others away. A group where no choice changes anything is dead as a whole.
                    int slash = entry.Path.LastIndexOf('/');
                    var folder = slash > 0 ? entry.Path[..slash] : entry.Path;
                    (List<AvatarToggle> Members, bool Carried, string Folder) group = groups.TryGetValue(entry.Group, out var known) ? known : (new List<AvatarToggle>(), false, folder);
                    group.Members.Add(toggle);
                    groups[entry.Group] = (group.Members, group.Carried || carried, group.Folder);
                }
                else if (!carried)
                {
                    _empty.Add(entry.Path);
                    toggle.Slot.Destroy();
                    _result.Toggles--;
                }
            }
            foreach (var (_, group) in groups)
            {
                if (group.Carried)
                    continue;
                _empty.Add(group.Folder);
                foreach (var member in group.Members)
                {
                    member.Slot.Destroy();
                    _result.Toggles--;
                }
            }

            foreach (var gesture in plan.Gestures)
            {
                var reader = EnsureReader();
                var slot = _gestures!.AddSlot($"{gesture.Hand} {gesture.Sign}");
                var trigger = slot.AttachComponent<GestureTrigger>();
                trigger.Reader.Target = reader;
                trigger.Hand.Value = gesture.Hand;
                trigger.Gesture.Value = gesture.Sign;
                trigger.UseFistWeight.Value = gesture.FistWeight;
                trigger.BlockBlinking.Value = gesture.BlockBlinking;
                int before = _carried;
                ApplyPose(gesture.On, gesture.Off, trigger.Active, trigger.Amount, SkinnedMeshRenderer.BlendShapePriorityGesture, 0.1f, slot);
                if (_carried == before)
                    slot.Destroy();
                else
                    _result.Gestures++;
            }

            foreach (var (what, count) in plan.Skipped)
                _skipped[what] = _skipped.GetValueOrDefault(what) + count;
        }

        private HandGestureReader EnsureReader()
        {
            if (_gestures == null)
            {
                _gestures = _model.AddSlot("Gestures");
                _reader = _gestures.AttachComponent<HandGestureReader>();
            }
            return _reader ??= _gestures.AttachComponent<HandGestureReader>();
        }

        // The pose the avatar shows with nothing switched: written in, so it is what the avatar saves as.
        // Blendshapes are the exception, because a mesh's shapes are only known once it has loaded; they
        // are held by a driver underneath every switch instead.
        private void ApplyRest(UnityPose pose)
        {
            foreach (var (binding, value) in pose.Floats)
            {
                var slot = _nodes.AtPath(binding.Path);
                if (slot == null)
                    continue;
                switch (Classify(binding))
                {
                    case Target.Active:
                        slot.ActiveSelf.Value = value > 0.5f;
                        break;
                    case Target.Enabled:
                        foreach (var component in EnabledTargets(slot, binding))
                            component.Enabled.Value = value > 0.5f;
                        break;
                    case Target.ConstraintWeight:
                    case Target.ConstraintSource:
                        if (ConstraintField(slot, binding, Classify(binding)) is { } weight)
                            weight.Value = value;
                        break;
                    case Target.BlendShape:
                        if (value != 0f)
                        {
                            _rest ??= (_menu ?? MenuSlot()).AddSlot("Rest");
                            var driver = _rest.AttachComponent<BlendShapeSwitchDriver>();
                            driver.On.Value = true;
                            driver.Renderer.Target = slot.GetComponent<SkinnedMeshRenderer>();
                            driver.Scope.Target = _model;
                            driver.BlendShape.Value = binding.Attribute["blendShape.".Length..];
                            driver.Weight.Value = System.Math.Clamp(value / 100f, 0f, 1f);
                            driver.Priority.Value = SkinnedMeshRenderer.BlendShapePriorityRest;
                        }
                        break;
                }
            }
            foreach (var (binding, value) in pose.Vectors)
            {
                if (binding.Path.Length == 0 || _nodes.AtPath(binding.Path) is not { } slot)
                    continue;
                switch (binding.Attribute)
                {
                    case "position": slot.LocalPosition.Value = Mirror(value) / _unit; break;
                    case "scale": slot.LocalScale.Value = value; break;
                    case "euler": slot.LocalRotation.Value = EulerDegrees(value); break;
                }
            }
            foreach (var (binding, value) in pose.Rotations)
                if (binding.Path.Length > 0 && _nodes.AtPath(binding.Path) is { } slot)
                    slot.LocalRotation.Value = MirrorRotation(value);
            foreach (var (binding, value) in pose.References)
            {
                if (MaterialSlot(binding) is { } materialSlot && value.Guid is { } guid && _materialFor?.Invoke(guid) is { } material)
                    materialSlot.Target = material;
            }
            RestMaterialValues(pose);
            foreach (var clip in pose.Motions)
                Motion(clip, null, MenuSlot());
        }

        private Slot MenuSlot()
        {
            if (_menu == null)
            {
                _menu = _model.AddSlot("Avatar Menu");
                _menu.AttachComponent<AvatarMenu>();
            }
            return _menu;
        }

        // Drivers for one switch: what its on pose sets, held while it is on; what its off pose sets
        // differently from rest, held while it is off.
        private void ApplyPose(UnityPose on, UnityPose off, IField<bool> sw, IField<float>? amount, int priority, float fade, Slot host)
        {
            MaterialValues(on, off, sw, host);
            foreach (var binding in on.Floats.Keys.Union(off.Floats.Keys).ToList())
            {
                float? onValue = on.Floats.TryGetValue(binding, out var a) ? a : null;
                float? offValue = off.Floats.TryGetValue(binding, out var b) ? b : null;
                Float(binding, onValue, offValue, sw, amount, priority, fade, host);
            }
            foreach (var binding in on.Vectors.Keys.Union(off.Vectors.Keys).ToList())
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
                bool hasOn = on.Vectors.TryGetValue(binding, out var onValue);
                bool hasOff = off.Vectors.TryGetValue(binding, out var offValue);
                switch (binding.Attribute)
                {
                    case "position":
                    {
                        var rest = slot.LocalPosition.Value;
                        Switch(slot.LocalPosition, hasOff ? Mirror(offValue) / _unit : rest, hasOn ? Mirror(onValue) / _unit : rest, sw, host);
                        break;
                    }
                    case "scale":
                    {
                        var rest = slot.LocalScale.Value;
                        Switch(slot.LocalScale, hasOff ? offValue : rest, hasOn ? onValue : rest, sw, host);
                        break;
                    }
                    case "euler":
                    {
                        var rest = slot.LocalRotation.Value;
                        Switch(slot.LocalRotation, hasOff ? EulerDegrees(offValue) : rest, hasOn ? EulerDegrees(onValue) : rest, sw, host);
                        break;
                    }
                }
            }
            foreach (var binding in on.Rotations.Keys.Union(off.Rotations.Keys).ToList())
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
                var onValue = on.Rotations.TryGetValue(binding, out var a) ? MirrorRotation(a) : rest;
                var offValue = off.Rotations.TryGetValue(binding, out var b) ? MirrorRotation(b) : rest;
                Switch(slot.LocalRotation, offValue, onValue, sw, host);
            }
            foreach (var binding in on.References.Keys.Union(off.References.Keys).ToList())
            {
                var materialSlot = MaterialSlot(binding);
                if (materialSlot == null)
                {
                    Skip("swaps of things other than materials");
                    continue;
                }
                var onMaterial = on.References.TryGetValue(binding, out var a) && a.Guid is { } onGuid ? _materialFor?.Invoke(onGuid) : null;
                var offMaterial = off.References.TryGetValue(binding, out var b) && b.Guid is { } offGuid ? _materialFor?.Invoke(offGuid) : null;
                SwapMaterial(materialSlot, offMaterial, onMaterial, sw, host);
            }
            foreach (var clip in on.Motions)
                Motion(clip, sw, host);
        }

        private enum Target { None, Active, Enabled, BlendShape, MaterialValue, Muscle, ConstraintWeight, ConstraintSource }

        private static readonly HashSet<int> ConstraintClasses = new() { 1773428102, 1818360608, 1818360609, 1818360610, 895512359, 1183024399 };

        private static Target Classify(UnityBinding binding)
        {
            if (binding.ClassId == 95)
                return Target.Muscle;
            if (ConstraintClasses.Contains(binding.ClassId) || binding.ClassId == 114 && IsVrcConstraintField(binding.Attribute))
            {
                if (binding.Attribute is "m_Weight" or "GlobalWeight")
                    return Target.ConstraintWeight;
                if (binding.Attribute is "m_Active" or "m_IsContraintActive" or "IsActive" or "m_Enabled")
                    return Target.Enabled;
                if (SourceIndex(binding.Attribute) >= 0)
                    return Target.ConstraintSource;
            }
            if (binding.Attribute == "m_IsActive")
                return Target.Active;
            if (binding.Attribute == "m_Enabled")
                return Target.Enabled;
            if (binding.Attribute.StartsWith("blendShape.", StringComparison.Ordinal))
                return Target.BlendShape;
            if (binding.Attribute.StartsWith("material.", StringComparison.Ordinal))
                return Target.MaterialValue;
            return Target.None;
        }

        private static bool IsVrcConstraintField(string attribute)
            => attribute is "GlobalWeight" or "IsActive" || attribute.StartsWith("Sources.source", StringComparison.Ordinal);

        // "m_Sources.Array.data[1].weight" or "Sources.source1.Weight": the source's index.
        private static int SourceIndex(string attribute)
        {
            string digits;
            if (attribute.StartsWith("m_Sources.Array.data[", StringComparison.Ordinal) && attribute.EndsWith("].weight", StringComparison.Ordinal))
                digits = attribute["m_Sources.Array.data[".Length..^"].weight".Length];
            else if (attribute.StartsWith("Sources.source", StringComparison.Ordinal) && attribute.EndsWith(".Weight", StringComparison.Ordinal))
                digits = attribute["Sources.source".Length..^".Weight".Length];
            else
                return -1;
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : -1;
        }

        // Our constraint for a Unity constraint curve on this object: the one of the matching kind, or the
        // first there is (the avatar constraint scripts share one class that says nothing about kind).
        private static TransformConstraint? ConstraintOn(Slot slot, UnityBinding binding)
        {
            var kind = binding.ClassId switch
            {
                1773428102 => ConstraintKind.Parent,
                1818360608 => ConstraintKind.Position,
                1818360609 => ConstraintKind.Rotation,
                1818360610 => ConstraintKind.Scale,
                895512359 => ConstraintKind.Aim,
                1183024399 => ConstraintKind.LookAt,
                _ => (ConstraintKind?)null,
            };
            TransformConstraint? first = null;
            foreach (var constraint in slot.GetComponents<TransformConstraint>())
            {
                if (kind == null || constraint.Kind.Value == kind)
                    return constraint;
                first ??= constraint;
            }
            return kind == null ? first : null;
        }

        // The field a constraint curve moves.
        private static Sync<float>? ConstraintField(Slot slot, UnityBinding binding, Target target)
        {
            var constraint = ConstraintOn(slot, binding);
            if (constraint == null)
                return null;
            if (target == Target.ConstraintWeight)
                return constraint.Weight;
            int index = SourceIndex(binding.Attribute);
            return index >= 0 && index < constraint.Sources.Count ? constraint.Sources[index].Weight : null;
        }

        // The components a Unity "enabled" curve means on this object: its renderer, the bone chains and
        // colliders its scripts became, or the constraint it is.
        private IEnumerable<Component> EnabledTargets(Slot slot, UnityBinding binding)
        {
            if (ConstraintClasses.Contains(binding.ClassId) || binding.ClassId == 114 && binding.Attribute == "IsActive")
            {
                if (ConstraintOn(slot, binding) is { } constraint)
                    yield return constraint;
                yield break;
            }
            switch (binding.ClassId)
            {
                case 137:
                    if (slot.GetComponent<SkinnedMeshRenderer>() is { } skinned)
                        yield return skinned;
                    break;
                case 23:
                    if (slot.GetComponent<MeshRenderer>() is { } mesh)
                        yield return mesh;
                    break;
                case 114:
                    if (_result.Scripts.TryGetValue(slot, out var built))
                        foreach (var component in built)
                            yield return component;
                    break;
                case 82:
                    if (AudioEnabledTarget(slot) is { } audio)
                        yield return audio;
                    break;
            }
        }

        private void Float(UnityBinding binding, float? onValue, float? offValue, IField<bool> sw, IField<float>? amount, int priority, float fade, Slot host)
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
                Logger.Debug($"UnityPackage: pose path '{binding.Path}' ({binding.Attribute}) is not an object here");
                return;
            }
            switch (kind)
            {
                case Target.Active:
                {
                    bool rest = slot.ActiveSelf.Value;
                    Switch(slot.ActiveSelf, offValue is { } f ? f > 0.5f : rest, onValue is { } n ? n > 0.5f : rest, sw, host);
                    return;
                }
                case Target.Enabled:
                {
                    bool any = false;
                    foreach (var component in EnabledTargets(slot, binding).ToList())
                    {
                        bool rest = component.Enabled.Value;
                        Switch(component.Enabled, offValue is { } f ? f > 0.5f : rest, onValue is { } n ? n > 0.5f : rest, sw, host);
                        any = true;
                    }
                    if (!any)
                    {
                        Skip(binding.ClassId == 114 ? "switches of scripts that do not come across" : "switches of other components");
                        Logger.Debug($"UnityPackage: pose switches class {binding.ClassId} script {binding.Script} on '{binding.Path}', nothing built there");
                    }
                    return;
                }
                case Target.ConstraintWeight:
                case Target.ConstraintSource:
                {
                    var weight = ConstraintField(slot, binding, kind);
                    if (weight == null)
                    {
                        Skip("constraint changes on constraints that do not come across");
                        return;
                    }
                    float rest = weight.Value;
                    Switch(weight, offValue ?? rest, onValue ?? rest, sw, host);
                    return;
                }
                case Target.BlendShape:
                {
                    var renderer = slot.GetComponent<SkinnedMeshRenderer>();
                    if (renderer == null)
                    {
                        Skip("shapes on meshes not in the package");
                        return;
                    }
                    var shape = binding.Attribute["blendShape.".Length..];
                    // The rest pose already holds whatever the avatar shows with nothing switched; an off
                    // value that matches it needs no driver of its own.
                    float restWeight = _restPose.Floats.TryGetValue(binding, out var r) ? r : 0f;
                    if (onValue is { } onWeight && MathF.Abs(onWeight - restWeight) > 1e-3f)
                        Shape(renderer, shape, onWeight, sw, invert: false, amount, priority, fade, host);
                    if (offValue is { } offWeight && MathF.Abs(offWeight - restWeight) > 1e-3f)
                        Shape(renderer, shape, offWeight, sw, invert: true, null, priority, fade, host);
                    return;
                }
            }
        }

        private void Shape(SkinnedMeshRenderer renderer, string shape, float weight, IField<bool> sw, bool invert, IField<float>? amount, int priority, float fade, Slot host)
        {
            var driver = host.AttachComponent<BlendShapeSwitchDriver>();
            driver.Switches.Add(sw);
            driver.Invert.Value = invert;
            driver.Amount.Target = amount!;
            driver.Renderer.Target = renderer;
            driver.Scope.Target = _model;
            driver.BlendShape.Value = shape;
            driver.Weight.Value = System.Math.Clamp(weight / 100f, 0f, 1f);
            driver.FadeTime.Value = fade;
            driver.Priority.Value = priority;
            _carried++;
        }

        // "m_Materials.Array.data[2]" on a renderer: its third material slot.
        private AssetRef<MaterialAsset>? MaterialSlot(UnityBinding binding)
        {
            const string prefix = "m_Materials.Array.data[";
            if (!binding.Attribute.StartsWith(prefix, StringComparison.Ordinal) || _nodes.AtPath(binding.Path) is not { } slot)
                return null;
            int close = binding.Attribute.IndexOf(']');
            if (close < 0 || !int.TryParse(binding.Attribute.AsSpan(prefix.Length, close - prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                return null;
            var materials = MaterialSlotsOf(slot);
            return index >= 0 && index < materials.Count ? materials[index] : null;
        }

        private void SwapMaterial(AssetRef<MaterialAsset> materialSlot, MaterialProvider? offMaterial, MaterialProvider? onMaterial, IField<bool> sw, Slot host)
        {
            var key = ((IWorldElement)materialSlot, "material");
            if (_switches.TryGetValue(key, out var existing) && existing is MaterialSwitchDriver shared)
            {
                shared.AddSwitch(sw, onMaterial ?? (IAssetProvider<MaterialAsset>?)shared.OffMaterial.Target);
                _carried++;
                return;
            }
            if (materialSlot.IsDriven)
            {
                Skip("material swaps on a slot something else drives");
                return;
            }
            var swap = host.AttachComponent<MaterialSwitchDriver>();
            swap.OffMaterial.Target = offMaterial ?? materialSlot.Target;
            swap.OnMaterial.Target = onMaterial ?? materialSlot.Target;
            swap.AddSwitch(sw, onMaterial ?? materialSlot.Target);
            swap.AddSlot(materialSlot);
            _switches[key] = swap;
            _carried++;
        }

        // A clip that moves: our own clip of the channels it moves that land somewhere here, played while
        // the switch is on (always, with no switch). Sampled at 30 a second; Unity's curves are smooth
        // between keys and ours interpolate linearly between those samples.
        private void Motion(UnityAnimationClip clip, IField<bool>? sw, Slot host)
        {
            var ours = new AnimationClip(clip.Name, MathF.Max(clip.Length, 1f / 30f));
            var targets = new List<IField>();
            var waiting = new List<(int Track, SkinnedMeshRenderer Renderer, string Shape)>();
            int frames = System.Math.Max(1, (int)MathF.Ceiling(clip.Length * 30f));
            float Time(int i) => MathF.Min(clip.Length, i / 30f);

            foreach (var curve in clip.Floats)
            {
                var binding = new UnityBinding(curve.Path, curve.ClassId, curve.Attribute, curve.Script);
                var slot = _nodes.AtPath(curve.Path);
                if (slot == null)
                    continue;
                switch (Classify(binding))
                {
                    case Target.Active:
                    {
                        var track = ours.AddCurveTrack<bool>(curve.Path, "Active");
                        for (int i = 0; i <= frames; i++)
                            track.AddKey(Time(i), curve.Sample(Time(i)) > 0.5f);
                        targets.Add(slot.ActiveSelf);
                        break;
                    }
                    case Target.Enabled:
                    {
                        foreach (var component in EnabledTargets(slot, binding))
                        {
                            var track = ours.AddCurveTrack<bool>(curve.Path, component.GetType().Name + ".Enabled");
                            for (int i = 0; i <= frames; i++)
                                track.AddKey(Time(i), curve.Sample(Time(i)) > 0.5f);
                            targets.Add(component.Enabled);
                        }
                        break;
                    }
                    case Target.BlendShape:
                    {
                        if (slot.GetComponent<SkinnedMeshRenderer>() is not { } renderer)
                            break;
                        var shape = curve.Attribute["blendShape.".Length..];
                        var track = ours.AddCurveTrack<float>(curve.Path, "BlendShape." + shape);
                        for (int i = 0; i <= frames; i++)
                            track.AddKey(Time(i), curve.Sample(Time(i)) / 100f);
                        waiting.Add((ours.TrackCount - 1, renderer, shape));
                        targets.Add(null!);
                        break;
                    }
                    case Target.ConstraintWeight:
                    case Target.ConstraintSource:
                    {
                        if (ConstraintField(slot, binding, Classify(binding)) is not { } weight)
                            break;
                        var track = ours.AddCurveTrack<float>(curve.Path, "Constraint." + curve.Attribute);
                        for (int i = 0; i <= frames; i++)
                            track.AddKey(Time(i), curve.Sample(Time(i)));
                        targets.Add(weight);
                        break;
                    }
                    case Target.MaterialValue:
                        break;
                    default:
                        Skip("animated " + curve.Attribute);
                        break;
                }
            }
            foreach (var curve in clip.Positions)
                if (curve.Path.Length > 0 && _nodes.AtPath(curve.Path) is { } slot)
                {
                    var track = ours.AddCurveTrack<float3>(curve.Path, "Position");
                    for (int i = 0; i <= frames; i++)
                        track.AddKey(Time(i), Mirror(curve.Sample(Time(i))) / _unit);
                    targets.Add(slot.LocalPosition);
                }
            foreach (var curve in clip.Scales)
                if (curve.Path.Length > 0 && _nodes.AtPath(curve.Path) is { } slot)
                {
                    var track = ours.AddCurveTrack<float3>(curve.Path, "Scale");
                    for (int i = 0; i <= frames; i++)
                        track.AddKey(Time(i), curve.Sample(Time(i)));
                    targets.Add(slot.LocalScale);
                }
            foreach (var curve in clip.Eulers)
                if (curve.Path.Length > 0 && _nodes.AtPath(curve.Path) is { } slot)
                {
                    var track = ours.AddCurveTrack<floatQ>(curve.Path, "Rotation");
                    for (int i = 0; i <= frames; i++)
                        track.AddKey(Time(i), EulerDegrees(curve.Sample(Time(i))));
                    targets.Add(slot.LocalRotation);
                }
            foreach (var curve in clip.Rotations)
                if (curve.Path.Length > 0 && _nodes.AtPath(curve.Path) is { } slot)
                {
                    var track = ours.AddCurveTrack<floatQ>(curve.Path, "Rotation");
                    for (int i = 0; i <= frames; i++)
                        track.AddKey(Time(i), MirrorRotation(curve.Sample(Time(i))));
                    targets.Add(slot.LocalRotation);
                }
            MaterialMotion(clip, ours, targets, frames, Time);
            if (clip.References.Count > 0)
                Skip("animated material swaps");
            if (ours.TrackCount == 0 || _localDB == null)
                return;

            var holder = host.AddSlot("Animation " + clip.Name);
            var provider = holder.AttachComponent<AnimationProvider>();
            var animator = holder.AttachComponent<Animator>();
            animator.Clip.Target = provider;
            animator.Playback.LoopMode = clip.Loop ? PlaybackLoopMode.Loop : PlaybackLoopMode.Once;
            animator.Playback.Play();
            if (sw != null)
                animator.Switches.Add(sw);
            int bound = 0, busy = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] is not { } field)
                    continue;
                if (field.IsDriven)
                {
                    busy++;
                    continue;
                }
                var entry = animator.Bindings.Add();
                entry.TrackIndex.Value = i;
                entry.Target.DriveTarget(field);
                bound++;
            }
            if (busy > 0)
                Skip("animated channels something else already moves");
            _result.Animations++;
            _carried++;

            var bytes = ours.ToBytes();
            var db = _localDB;
            var world = holder.World;
            world?.StartTask(async () =>
            {
                await WorldContext.ToBackground();
                string uri;
                try
                {
                    uri = await db.SaveAssetAsync(bytes, ".lanim");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"UnityPackage: animation '{clip.Name}' could not be stored: {ex.Message}");
                    return;
                }
                await WorldContext.ToWorld();
                if (!provider.IsDestroyed && !string.IsNullOrEmpty(uri))
                    provider.URL.Value = new Uri(uri);
                // A mesh's shapes arrive with the mesh, so a shape channel is wired once they are there.
                for (int attempt = 0; attempt < 120 && waiting.Count > 0 && !animator.IsDestroyed; attempt++)
                {
                    for (int w = waiting.Count - 1; w >= 0; w--)
                    {
                        var (track, renderer, shape) = waiting[w];
                        if (renderer.IsDestroyed)
                        {
                            waiting.RemoveAt(w);
                            continue;
                        }
                        if (Animator.ResolveProperty(renderer.Slot, "BlendShape." + shape) is { IsDriven: false } field)
                        {
                            var entry = animator.Bindings.Add();
                            entry.TrackIndex.Value = track;
                            entry.Target.DriveTarget(field);
                            waiting.RemoveAt(w);
                        }
                    }
                    if (waiting.Count == 0)
                        break;
                    await WorldContext.ToBackground();
                    await System.Threading.Tasks.Task.Delay(500);
                    await WorldContext.ToWorld();
                }
            });
            Logger.Debug($"UnityPackage: animation '{clip.Name}' -> {ours.TrackCount} tracks, {bound} bound now, {waiting.Count} waiting on mesh shapes, {busy} already moved by something else");
        }

        // Unity's angles in degrees, turned about Z, then X, then Y, then mirrored into ours.
        private static floatQ EulerDegrees(float3 degrees)
        {
            var e = degrees * (MathF.PI / 180f);
            var q = floatQ.AxisAngle(new float3(0f, 1f, 0f), e.y)
                * floatQ.AxisAngle(new float3(1f, 0f, 0f), e.x)
                * floatQ.AxisAngle(new float3(0f, 0f, 1f), e.z);
            return MirrorRotation(q);
        }
    }
}
