// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Components.Utility;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// The avatar's switches, gestures and constraints, rebuilt from what the package saved.
//
// A VRCFury toggle becomes an AvatarToggle in the avatar's menu with a driver per thing it changes; a
// flipbook selector becomes a folder of choices that replace each other. A gesture becomes a
// GestureTrigger driving the same kinds of drivers. The package's constraints, of either kind, become
// TransformConstraints. Drivers on one field are shared: two outfits that both hide the boots make one
// driver with two switches, because a field takes one driver. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private readonly UnityImportCandidate _candidate;
        private readonly Slot _model;
        private readonly NodeIndex _nodes;
        private readonly float _unit;
        private readonly UnityImportResult _result;
        private readonly Func<string, MaterialProvider?>? _materialFor;
        private readonly Func<string, UnityAnimationClip?>? _clipFor;
        private readonly LocalDB? _localDB;
        private readonly Dictionary<(IWorldElement Field, string Value), Component> _switches = new();
        private readonly SortedDictionary<string, int> _skipped = new(StringComparer.Ordinal);
        private readonly List<string> _empty = new();
        private int _carried;
        private Slot? _menu;
        private Slot? _gestures;
        private HandGestureReader? _reader;

        public FeatureBuilder(UnityImportCandidate candidate, Slot model, NodeIndex nodes, float unit, UnityImportResult result,
            Func<string, MaterialProvider?>? materialFor, Func<string, UnityAnimationClip?>? clipFor, LocalDB? localDB)
        {
            _clipFor = clipFor;
            _localDB = localDB;
            _candidate = candidate;
            _model = model;
            _nodes = nodes;
            _unit = unit;
            _result = result;
            _materialFor = materialFor;
        }

        public void Finish()
        {
            if (_empty.Count > 0)
                _result.Notes.Add($"Left out of the menu, nothing they change comes across: {string.Join(", ", _empty)}.");
            if (_skipped.Count > 0)
                _result.Notes.Add("Not carried: " + string.Join(", ", _skipped.Select(s => s.Value > 1 ? $"{s.Key} x{s.Value}" : s.Key)) + ".");
        }

        private void Skip(string what) => _skipped[what] = _skipped.GetValueOrDefault(what) + 1;

        // ---- Toggles ----

        public void Toggles()
        {
            foreach (var part in _candidate.Toggles)
            {
                try
                {
                    Toggle(part);
                }
                catch (Exception ex)
                {
                    Skip("broken toggles");
                    Logger.Warn($"UnityPackage: a toggle on '{part.Node.Name}' could not be built: {ex.Message}");
                }
            }
        }

        private void Toggle(UnityComponentRef part)
        {
            var data = part.Data!;
            _actionRoot = part.Node;
            var references = UnityVrcFury.References(part.Doc);
            if (part.Feature == "WorldConstraint")
            {
                var target = _nodes.Resolve(part.Node);
                var drop = NewToggle(data["menuPath"]?.Text ?? part.Node.Name, false, "");
                if (target != null)
                    WorldDrop(target, drop.State, drop.Slot);
                return;
            }

            var path = data["name"]?.Text ?? "";
            bool on = (data["defaultOn"]?.Int() ?? 0) != 0;
            var group = (data["enableExclusiveTag"]?.Int() ?? 0) != 0 ? data["exclusiveTag"]?.Text ?? "" : "";
            var actions = UnityVrcFury.Actions(data["state"], references).ToList();
            var plain = actions.Where(a => a.Class != "FlipBookBuilderAction").ToList();
            var flipbook = actions.FirstOrDefault(a => a.Class == "FlipBookBuilderAction");
            // The parameter it sets for others to read, when it names one: the merged layers' answer to it
            // already made the switch, which then takes this toggle's own actions as well.
            var global = (data["useGlobalParam"]?.Int() ?? 0) != 0 ? data["globalParam"]?.Text?.Trim() : null;
            var icon = (data["enableIcon"]?.Int() ?? 0) != 0 ? UnityVrcFury.Guid(data["icon"]) : null;
            ToggleExtras(data);

            // A selector: the slider picks one page. Each page becomes a choice, and choosing one puts the
            // others away; the page the slider rests on starts chosen.
            if ((data["slider"]?.Int() ?? 0) != 0 && flipbook.Data?["pages"] is UnitySeq { Items.Count: > 0 } pages)
            {
                int count = pages.Items.Count;
                int start = (int)MathF.Round(System.Math.Clamp(data["defaultSliderValue"]?.Float() ?? 0f, 0f, 1f) * (count - 1));
                for (int p = 0; p < count; p++)
                {
                    var choice = NewToggle($"{path}/{p + 1}", p == start, "pages:" + path);
                    foreach (var action in plain.Concat(UnityVrcFury.Actions(pages.Items[p]["state"], references)))
                        Action(action, choice.State, null, SkinnedMeshRenderer.BlendShapePriorityToggle, 0f, choice.Slot);
                }
                return;
            }

            // A slider: one dial that plays the actions by how far along it is, from rest at 0 to themselves at
            // the top (see UnityAvatarSliders).
            if ((data["slider"]?.Int() ?? 0) != 0)
            {
                SliderToggle(path, System.Math.Clamp(data["defaultSliderValue"]?.Float() ?? 0f, 0f, 1f), plain, global, icon);
                return;
            }

            var adopted = global != null && _toggleByParameter.TryGetValue(global, out var made) && !made.IsDestroyed ? made : null;
            var toggle = adopted ?? NewToggle(path, on, group);
            if (adopted != null)
            {
                adopted.State.Value = on;
                if (group.Length > 0)
                    adopted.ExclusiveGroup.Value = group;
            }
            if ((data["holdButton"]?.Int() ?? 0) != 0)
                HoldButton(toggle);
            if (icon != null)
                _furyIcons.Add((toggle.Icon, icon));
            // An off state is the group's "none": on with nothing else of the group on.
            IField<bool> state = toggle.State;
            if (group.Length > 0 && (data["exclusiveOffState"]?.Int() ?? 0) != 0)
                state = OffState(toggle, group);
            float fade = (data["hasTransition"]?.Int() ?? 0) != 0 ? MathF.Max(0f, data["transitionTimeIn"]?.Float() ?? 0f) : 0f;
            // A parameter the live graph reads without any pose answering it (a speed, a sound) is fed all the same.
            bool feeds = adopted == null && Feed(global, toggle);
            int before = _carried;
            foreach (var action in actions)
                Action(action, state, null, SkinnedMeshRenderer.BlendShapePriorityToggle, fade, toggle.Slot);
            if (_carried == before && adopted == null && !feeds)
            {
                _empty.Add(path);
                toggle.Slot.Destroy();
                _result.Toggles--;
            }
        }

        private AvatarToggle NewToggle(string path, bool on, string group)
        {
            if (_menu == null)
            {
                _menu = _model.AddSlot("Avatar Menu");
                _menu.AttachComponent<AvatarMenu>();
            }
            var slot = _menu.AddSlot(string.IsNullOrEmpty(path) ? "Toggle" : path);
            var toggle = slot.AttachComponent<AvatarToggle>();
            toggle.Path.Value = path;
            toggle.State.Value = on;
            toggle.ExclusiveGroup.Value = group;
            _result.Toggles++;
            return toggle;
        }

        // ---- Gestures ----

        public void Gestures()
        {
            foreach (var part in _candidate.Gestures)
            {
                if (part.Data?["gestures"] is not UnitySeq list)
                    continue;
                var references = UnityVrcFury.References(part.Doc);
                _actionRoot = part.Node;
                if (_gestures == null)
                {
                    _gestures = _model.AddSlot("Gestures");
                    _reader = _gestures.AttachComponent<HandGestureReader>();
                }
                foreach (var gesture in list.Items)
                {
                    var hand = (GestureHand)System.Math.Clamp(gesture["hand"]?.Int() ?? 0, 0, 3);
                    var sign = (HandGesture)System.Math.Clamp(gesture["sign"]?.Int() ?? 0, 0, 7);
                    var combo = (HandGesture)System.Math.Clamp(gesture["comboSign"]?.Int() ?? 0, 0, 7);
                    var slot = _gestures.AddSlot(hand == GestureHand.Both ? $"{sign} + {combo}" : $"{hand} {sign}");
                    var trigger = slot.AttachComponent<GestureTrigger>();
                    trigger.Reader.Target = _reader!;
                    trigger.Hand.Value = hand;
                    trigger.Gesture.Value = sign;
                    trigger.OtherGesture.Value = combo;
                    trigger.UseFistWeight.Value = (gesture["enableWeight"]?.Int() ?? 0) != 0;
                    trigger.BlockBlinking.Value = (gesture["disableBlinking"]?.Int() ?? 0) != 0;
                    float fade = (gesture["customTransitionTime"]?.Int() ?? 0) != 0 ? MathF.Max(0f, gesture["transitionTime"]?.Float() ?? 0f) : 0.1f;
                    foreach (var action in UnityVrcFury.Actions(gesture["state"], references))
                        Action(action, trigger.Active, trigger.Amount, SkinnedMeshRenderer.BlendShapePriorityGesture, fade, slot);
                    _result.Gestures++;
                }
            }
        }

        // ---- Actions ----

        // Scale is how far along a slider the switch sits (1 for a plain switch); sampled reads a clip at that
        // point rather than playing it. Values blend toward the action's; on/off and swaps flip at half.
        private void Action(UnityVrcFury.Entry action, IField<bool> on, IField<float>? amount, int priority, float fade, Slot host,
            float scale = 1f, bool sampled = false)
        {
            var data = action.Data;
            switch (action.Class)
            {
                case "ObjectToggleAction":
                {
                    if (scale < 0.5f)
                        return;
                    if (Object(data["obj"]) is not { } target)
                    {
                        Skip("actions on objects not in the package");
                        return;
                    }
                    // At rest an object a switch turns on is off, whatever the project left it as. One a
                    // switch turns off, or flips, rests the way the project left it: a creator who hid a
                    // spare tail in the scene and gave a switch the job of hiding it again meant it hidden.
                    int mode = data["mode"]?.Int() ?? 0;
                    bool rest = mode == 0 ? false : target.ActiveSelf.Value;
                    bool wanted = mode switch { 0 => true, 1 => false, _ => !rest };
                    if (rest != wanted)
                        Switch(target.ActiveSelf, rest, wanted, on, host);
                    if (mode == 0)
                        target.ActiveSelf.Value = rest;
                    return;
                }
                case "ScaleAction":
                {
                    if (Object(data["obj"]) is not { } target)
                    {
                        Skip("actions on objects not in the package");
                        return;
                    }
                    var rest = target.LocalScale.Value;
                    float factor = data["scale"]?.Float(1f) ?? 1f;
                    Switch(target.LocalScale, rest, rest * (1f + (factor - 1f) * scale), on, host);
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
                    var driver = host.AttachComponent<BlendShapeSwitchDriver>();
                    driver.Switches.Add(on);
                    driver.Amount.Target = amount!;
                    driver.Renderer.Target = renderer!;
                    driver.Scope.Target = _model;
                    driver.BlendShape.Value = shape;
                    driver.Weight.Value = System.Math.Clamp((data["blendShapeValue"]?.Float(100f) ?? 100f) / 100f * scale, 0f, 1f);
                    driver.FadeTime.Value = fade;
                    driver.Priority.Value = priority;
                    _carried++;
                    return;
                }
                case "MaterialPropertyAction":
                    MaterialProperty(data, on, scale, host);
                    return;
                case "MaterialAction":
                {
                    if (scale < 0.5f)
                        return;
                    var guid = UnityVrcFury.Guid(data["mat"]);
                    var holder = Component(data["renderer"]) ?? Object(data["obj"]);
                    var materials = holder != null ? MaterialSlotsOf(holder) : new List<AssetRef<MaterialAsset>>();
                    int index = data["materialIndex"]?.Int() ?? 0;
                    var material = guid != null ? _materialFor?.Invoke(guid) : null;
                    if (material == null || index < 0 || index >= materials.Count)
                    {
                        Skip("material swaps");
                        return;
                    }
                    var materialSlot = materials[index];
                    var key = ((IWorldElement)materialSlot, "material");
                    if (_switches.TryGetValue(key, out var existing) && existing is MaterialSwitchDriver shared)
                    {
                        shared.AddSwitch(on, material);
                        _carried++;
                        return;
                    }
                    if (materialSlot.IsDriven)
                    {
                        Skip("material swaps on a slot something else drives");
                        return;
                    }
                    var swap = host.AttachComponent<MaterialSwitchDriver>();
                    swap.OffMaterial.Target = materialSlot.Target;
                    swap.OnMaterial.Target = material;
                    swap.AddSwitch(on, material);
                    swap.AddSlot(materialSlot);
                    _switches[key] = swap;
                    _carried++;
                    return;
                }
                case "AnimationClipAction":
                {
                    var clip = ActionClip(data["clip"]);
                    if (clip == null)
                    {
                        Skip("animation clips not in the package");
                        return;
                    }
                    var pose = new UnityPose();
                    pose.Add(clip, sampled ? scale * clip.Length : 0f, play: !sampled);
                    ApplyPose(pose, new UnityPose(), on, amount, priority, fade, host);
                    return;
                }
                case "WorldDropAction":
                {
                    if (Object(data["obj"]) is { } target)
                        WorldDrop(target, on, host);
                    else
                        Skip("actions on objects not in the package");
                    return;
                }
                case "BlockBlinkingAction":
                case "BlockVisemesAction":
                    if (scale < 0.5f)
                        return;
                    FaceBlock(on, host, blinking: action.Class == "BlockBlinkingAction");
                    return;
                default:
                    Skip(Plain(action.Class));
                    return;
            }
        }

        // One driver per field; each switch that touches it adds itself with the value it wants.
        private void Switch<T>(Sync<T> field, T rest, T wanted, IField<bool> on, Slot host)
        {
            var key = ((IWorldElement)field, "value");
            if (_switches.TryGetValue(key, out var existing) && existing is ValueSwitchDriver<T> shared)
            {
                shared.AddSwitch(on, wanted);
                _carried++;
                return;
            }
            if (field.IsDriven)
            {
                Skip("changes to values something else drives");
                return;
            }
            var driver = host.AttachComponent<ValueSwitchDriver<T>>();
            driver.OffValue.Value = rest;
            driver.OnValue.Value = wanted;
            driver.AddSwitch(on, wanted);
            driver.AddDrive(field);
            _switches[key] = driver;
            _carried++;
        }

        // Pins the object in the world while the switch is on. Its pose until then is the one it has now.
        private void WorldDrop(Slot target, IField<bool> on, Slot host)
        {
            var constraint = target.GetComponent<TransformConstraint>();
            if (constraint == null)
            {
                constraint = target.AttachComponent<TransformConstraint>();
                constraint.Kind.Value = ConstraintKind.Parent;
                constraint.RestPosition.Value = target.LocalPosition.Value;
                constraint.RestRotation.Value = target.LocalRotation.Value;
                constraint.RestScale.Value = target.LocalScale.Value;
            }
            Switch(constraint.FreezeToWorld, false, true, on, host);
        }

        private Slot? Object(UnityNode? reference)
        {
            long id = reference?.Ref.FileId ?? 0;
            return id != 0 && _candidate.ResolveObject(id) is { } node ? _nodes.Resolve(node) : null;
        }

        private Slot? Component(UnityNode? reference)
        {
            long id = reference?.Ref.FileId ?? 0;
            return id != 0 && _candidate.ResolveComponent(id) is { } node ? _nodes.Resolve(node) : null;
        }

        private static string Plain(string actionClass) => actionClass switch
        {
            "FlipBookBuilderAction" => "flipbooks",
            "FlipbookAction" => "flipbooks",
            "PoiyomiUVTileAction" => "UV tile switches",
            "FxFloatAction" => "parameter changes",
            "DisableGesturesAction" => "gesture blocks",
            "ResetPhysboneAction" => "bone resets",
            _ => actionClass,
        };

        // ---- Constraints ----

        public void Constraints()
        {
            foreach (var part in _candidate.Constraints)
            {
                try
                {
                    if (Constraint(part))
                        _result.Constraints++;
                    else
                        Skip("constraints with nothing to hold");
                }
                catch (Exception ex)
                {
                    Skip("broken constraints");
                    Logger.Warn($"UnityPackage: a constraint on '{part.Node.Name}' could not be built: {ex.Message}");
                }
            }
        }

        private bool Constraint(UnityComponentRef part)
        {
            var d = part.Doc;
            bool vrc = part.Feature.StartsWith("Vrc", StringComparison.Ordinal);
            var kind = (vrc ? part.Feature[3..] : part.Feature) switch
            {
                "Position" => ConstraintKind.Position,
                "Rotation" => ConstraintKind.Rotation,
                "Scale" => ConstraintKind.Scale,
                "Aim" => ConstraintKind.Aim,
                "LookAt" => ConstraintKind.LookAt,
                _ => ConstraintKind.Parent,
            };

            var target = _nodes.Resolve(part.Node);
            if (vrc && d["TargetTransform"]?.Ref.FileId is long other and not 0)
                target = _candidate.ResolveTransform(other) is { } node ? _nodes.Resolve(node) : null;
            else if (RetargetBone(part.Node) is { } bone)
                target = bone;
            if (target == null)
                return false;

            var c = target.AttachComponent<TransformConstraint>();
            c.Kind.Value = kind;
            if (vrc)
                Vrc(c, d, kind);
            else
                Unity(c, d, kind);
            if (c.Sources.Count == 0 && !c.FreezeToWorld.Value)
            {
                c.Destroy();
                return false;
            }
            return true;
        }

        private void Unity(TransformConstraint c, UnityDocument d, ConstraintKind kind)
        {
            c.Weight.Value = d["m_Weight"]?.Float(1f) ?? 1f;
            c.Enabled.Value = (d["m_Enabled"]?.Int(1) ?? 1) != 0 && ((d["m_Active"] ?? d["m_IsContraintActive"])?.Int(1) ?? 1) != 0;

            var positionOffsets = d["m_TranslationOffsets"] as UnitySeq;
            var rotationOffsets = d["m_RotationOffsets"] as UnitySeq;
            if (d["m_Sources"] is UnitySeq sources)
            {
                for (int i = 0; i < sources.Items.Count; i++)
                {
                    var item = sources.Items[i];
                    if (Transform(item["sourceTransform"]) is not { } source)
                        continue;
                    var entry = c.AddSource(source, item["weight"]?.Float(1f) ?? 1f);
                    if (kind == ConstraintKind.Parent)
                    {
                        if (positionOffsets != null && i < positionOffsets.Items.Count)
                            entry.PositionOffset.Value = Mirror(Vector(positionOffsets.Items[i])) / _unit;
                        if (rotationOffsets != null && i < rotationOffsets.Items.Count)
                            entry.RotationOffset.Value = Euler(rotationOffsets.Items[i]);
                    }
                }
            }

            c.RestPosition.Value = Mirror(Vector(d["m_TranslationAtRest"])) / _unit;
            c.RestRotation.Value = Euler(d["m_RotationAtRest"]);
            c.RestScale.Value = VectorOr(d["m_ScaleAtRest"], float3.One);
            c.PositionOffset.Value = Mirror(Vector(d["m_TranslationOffset"])) / _unit;
            c.RotationOffset.Value = Euler(d["m_RotationOffset"]);
            c.ScaleOffset.Value = VectorOr(d["m_ScaleOffset"], float3.One);
            Axes(c.AffectPositionX, c.AffectPositionY, c.AffectPositionZ, d, "m_AffectTranslation");
            Axes(c.AffectRotationX, c.AffectRotationY, c.AffectRotationZ, d, "m_AffectRotation");
            Axes(c.AffectScaleX, c.AffectScaleY, c.AffectScaleZ, d, "m_AffectScaling");

            if (kind == ConstraintKind.Aim)
            {
                c.AimAxis.Value = Mirror(VectorOr(d["m_AimVector"], new float3(0f, 0f, 1f)));
                c.UpAxis.Value = Mirror(VectorOr(d["m_UpVector"], new float3(0f, 1f, 0f)));
                c.WorldUp.Value = Mirror(VectorOr(d["m_WorldUpVector"], new float3(0f, 1f, 0f)));
                c.UpObject.Target = Transform(d["m_WorldUpObject"])!;
                c.UpMode.Value = Up(d["m_UpType"]?.Int() ?? 0);
            }
            else if (kind == ConstraintKind.LookAt)
            {
                // Mirroring X turns a roll about forward the other way.
                c.Roll.Value = -(d["m_Roll"]?.Float() ?? 0f);
                c.UpObject.Target = Transform(d["m_WorldUpObject"])!;
                c.UpMode.Value = (d["m_UseUpObject"]?.Int() ?? 0) != 0 && c.UpObject.Target != null ? ConstraintUp.ObjectRotationUp : ConstraintUp.World;
            }
        }

        private void Vrc(TransformConstraint c, UnityDocument d, ConstraintKind kind)
        {
            c.Weight.Value = d["GlobalWeight"]?.Float(1f) ?? 1f;
            c.Enabled.Value = (d["m_Enabled"]?.Int(1) ?? 1) != 0 && (d["IsActive"]?.Int(1) ?? 1) != 0;
            c.LocalSpace.Value = (d["SolveInLocalSpace"]?.Int() ?? 0) != 0;
            c.FreezeToWorld.Value = (d["FreezeToWorld"]?.Int() ?? 0) != 0;

            foreach (var item in VrcSources(d["Sources"]))
            {
                if (Transform(item["SourceTransform"]) is not { } source)
                    continue;
                var entry = c.AddSource(source, item["Weight"]?.Float(1f) ?? 1f);
                if (kind == ConstraintKind.Parent)
                {
                    entry.PositionOffset.Value = Mirror(Vector(item["ParentPositionOffset"])) / _unit;
                    entry.RotationOffset.Value = Euler(item["ParentRotationOffset"]);
                }
            }

            c.RestPosition.Value = Mirror(Vector(d["PositionAtRest"])) / _unit;
            c.RestRotation.Value = Euler(d["RotationAtRest"]);
            c.RestScale.Value = VectorOr(d["ScaleAtRest"], float3.One);
            c.PositionOffset.Value = Mirror(Vector(d["PositionOffset"])) / _unit;
            c.RotationOffset.Value = Euler(d["RotationOffset"]);
            c.ScaleOffset.Value = VectorOr(d["ScaleOffset"], float3.One);
            Axes(c.AffectPositionX, c.AffectPositionY, c.AffectPositionZ, d, "AffectsPosition");
            Axes(c.AffectRotationX, c.AffectRotationY, c.AffectRotationZ, d, "AffectsRotation");
            Axes(c.AffectScaleX, c.AffectScaleY, c.AffectScaleZ, d, "AffectsScale");

            if (kind == ConstraintKind.Aim)
            {
                c.AimAxis.Value = Mirror(VectorOr(d["AimAxis"], new float3(0f, 0f, 1f)));
                c.UpAxis.Value = Mirror(VectorOr(d["UpAxis"], new float3(0f, 1f, 0f)));
                c.WorldUp.Value = Mirror(VectorOr(d["WorldUpVector"], new float3(0f, 1f, 0f)));
                c.UpObject.Target = Transform(d["WorldUpTransform"])!;
                c.UpMode.Value = Up(d["WorldUp"]?.Int() ?? 0);
            }
            else if (kind == ConstraintKind.LookAt)
            {
                c.Roll.Value = -(d["Roll"]?.Float() ?? 0f);
                c.UpObject.Target = Transform(d["WorldUpTransform"])!;
                c.UpMode.Value = (d["UseUpTransform"]?.Int() ?? 0) != 0 && c.UpObject.Target != null ? ConstraintUp.ObjectRotationUp : ConstraintUp.World;
            }
        }

        // The source list is keyed for animation: source0, source1, ... up to totalLength, the rest in an
        // overflow list. A plain list is read as one too.
        private static IEnumerable<UnityNode> VrcSources(UnityNode? sources)
        {
            if (sources is UnitySeq seq)
            {
                foreach (var item in seq.Items)
                    yield return item;
                yield break;
            }
            if (sources == null)
                yield break;
            int total = sources["totalLength"]?.Int() ?? 0;
            var overflow = sources["overflowList"] as UnitySeq;
            for (int i = 0; i < total; i++)
            {
                var item = sources["source" + i];
                if (item == null && overflow != null)
                {
                    int at = i - 16;
                    if (at >= 0 && at < overflow.Items.Count)
                        item = overflow.Items[at];
                }
                if (item != null)
                    yield return item;
            }
        }

        private Slot? Transform(UnityNode? reference)
        {
            long id = reference?.Ref.FileId ?? 0;
            return id != 0 && _candidate.ResolveTransform(id) is { } node ? _nodes.Resolve(node) : null;
        }

        private static void Axes(Sync<bool> x, Sync<bool> y, Sync<bool> z, UnityDocument d, string prefix)
        {
            x.Value = (d[prefix + "X"]?.Int(1) ?? 1) != 0;
            y.Value = (d[prefix + "Y"]?.Int(1) ?? 1) != 0;
            z.Value = (d[prefix + "Z"]?.Int(1) ?? 1) != 0;
        }

        private static ConstraintUp Up(int unity) => unity switch
        {
            1 => ConstraintUp.ObjectUp,
            2 => ConstraintUp.ObjectRotationUp,
            3 => ConstraintUp.Vector,
            4 => ConstraintUp.None,
            _ => ConstraintUp.World,
        };

        // Unity's angles in degrees, turned about Z, then X, then Y, then mirrored into ours.
        private static floatQ Euler(UnityNode? node)
        {
            if (node == null)
                return floatQ.Identity;
            var e = Vector(node) * (MathF.PI / 180f);
            var q = floatQ.AxisAngle(new float3(0f, 1f, 0f), e.y)
                * floatQ.AxisAngle(new float3(1f, 0f, 0f), e.x)
                * floatQ.AxisAngle(new float3(0f, 0f, 1f), e.z);
            return MirrorRotation(q);
        }

        private static float3 VectorOr(UnityNode? node, float3 fallback) => node == null ? fallback : Vector(node);
    }
}
