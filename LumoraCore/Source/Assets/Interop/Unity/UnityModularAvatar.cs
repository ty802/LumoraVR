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

// Modular Avatar, done at import the way it is done at upload. An outfit's armature is merged onto the body's
// bones by name (with the prefix and suffix it names stripped), a prop is sent to the bone it names, a
// replacement takes the place of what it replaces, a scale adjuster resizes its bone and not the bones under
// it, and a world-fixed object stays where it is in the world.
//
// The menu it builds from objects becomes the avatar's own: a menu item is a switch (a button one held down, a
// radial a dial) filed under the folder its installer names, and a submenu of children is a folder of them.
// Object toggles and shape changers are reactive: on while the nearest menu item above them is on, or always
// with none; inverted the other way round; the deepest one wins where two set the same thing. A shape
// changer's delete has no vertices to delete here, so the shape is set full instead, which is what such
// shapes are made for. Merged animators, menus from menu assets and parameter renames go through the menu
// plan with VRCFury's merged controllers. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        private const int LastHumanBone = 55;

        // The slot an object reference names: the object itself, or its path from the avatar's root.
        private Slot? AvatarObject(UnityNode? reference)
        {
            if (reference == null)
                return null;
            long id = reference["targetObject"]?.Ref.FileId ?? 0;
            if (id != 0 && _candidate.ResolveObject(id) is { } node && _nodes.Resolve(node) is { } slot)
                return slot;
            var path = reference["referencePath"]?.Text ?? "";
            if (path == UnityModularAvatarPlan.AvatarRoot)
                return _nodes.ImportRoot ?? _nodes.Top ?? _model;
            return path.Length > 0 ? _nodes.AtPath(path) : null;
        }

        private void ModularArrange(UnityBuildTools.Parts tools)
        {
            foreach (var (kind, part) in tools.ModularAvatar)
            {
                try
                {
                    switch (kind)
                    {
                        case UnityBuildTools.Ma.ReplaceObject: Replace(part); break;
                        case UnityBuildTools.Ma.MergeArmature: MergeArmature(part); break;
                        case UnityBuildTools.Ma.BoneProxy: BoneProxy(part); break;
                        case UnityBuildTools.Ma.ScaleAdjuster: ScaleAdjuster(part); break;
                        case UnityBuildTools.Ma.WorldFixedObject: WorldFixed(part); break;
                    }
                }
                catch (Exception ex)
                {
                    Skip("broken Modular Avatar components");
                    Logging.Logger.Warn($"UnityPackage: {kind} on '{part.Node.Name}' could not be applied: {ex.Message}");
                }
            }
        }

        // Every object the project wrote below this one, made now: the reader makes an object only when
        // something asks for it, and a merge or a replacement moves the whole branch.
        private void Branch(Slot? slot)
        {
            if (slot == null || PathTo(slot) is not { } path)
                return;
            foreach (var (other, node) in _candidate.NodesByPath)
                if (other.StartsWith(path + "/", StringComparison.Ordinal))
                    _nodes.Resolve(node);
        }

        // A slot's path from the avatar's root, as the project wrote it: a placed model is named by its own slot
        // and stepped through to its nodes, past the reader's root node.
        private string? PathTo(Slot slot)
        {
            var top = _nodes.PathTop ?? _nodes.Top;
            var chain = new List<Slot>();
            for (var at = slot; !ReferenceEquals(at, top); at = at.Parent)
            {
                if (at == null)
                    return null;
                chain.Add(at);
            }
            chain.Reverse();
            var names = new List<string>();
            for (int i = 0; i < chain.Count; i++)
            {
                names.Add(chain[i].SlotName.Value ?? "");
                var inner = _nodes.Inside(chain[i]);
                if (!ReferenceEquals(inner, chain[i]) && chain.IndexOf(inner, i + 1) is int j and > 0)
                    i = j;
            }
            return string.Join('/', names);
        }

        private void Replace(UnityComponentRef part)
        {
            var replacement = _nodes.Resolve(part.Node);
            var target = AvatarObject(part.Doc["targetObject"]);
            Branch(target);
            if (replacement == null || target == null || target.Parent == null || ReferenceEquals(replacement, target)
                || target.IsDescendantOf(replacement) || replacement.IsDescendantOf(target) && !ReferenceEquals(replacement.Parent, target))
            {
                Skip("replacements of objects not in the package");
                return;
            }
            replacement.SetParent(target.Parent, preserveGlobalTransform: true);
            foreach (var child in target.Children.ToList())
                child.SetParent(replacement, preserveGlobalTransform: true);
            target.Destroy();
        }

        private void MergeArmature(UnityComponentRef part)
        {
            var outfit = _nodes.Resolve(part.Node);
            var body = AvatarObject(part.Doc["mergeTarget"]);
            Branch(outfit);
            if (outfit == null || body == null || ReferenceEquals(outfit, body) || body.IsDescendantOf(outfit))
            {
                Skip("armature merges onto bones not in the package");
                return;
            }
            var link = outfit.AttachComponent<ArmatureLink>();
            link.Avatar.Target = body;
            link.Clothing.Target = outfit;
            link.KeepPose.Value = true;
            link.NamePrefix.Value = part.Doc["prefix"]?.Text ?? "";
            link.NameSuffix.Value = part.Doc["suffix"]?.Text ?? "";
            link.Link();
            _result.Links++;
        }

        private void BoneProxy(UnityComponentRef part)
        {
            var d = part.Doc;
            var prop = _nodes.Resolve(part.Node);
            int bone = d["boneReference"]?.Int(LastHumanBone) ?? LastHumanBone;
            var subPath = d["subPath"]?.Text ?? "";
            Slot? target;
            if (bone == LastHumanBone)
                target = subPath == "$$AVATAR" ? _nodes.ImportRoot ?? _nodes.Top : subPath.Length > 0 ? _nodes.AtPath(subPath) : null;
            else
                target = HumanBone(bone) is { } at && subPath.Length > 0 ? Descend(at, subPath) : HumanBone(bone);
            if (target == null && d["target"]?.Ref.FileId is long legacy and not 0)
                target = _candidate.ResolveTransform(legacy) is { } node ? _nodes.Resolve(node) : null;
            if (prop == null || target == null || ReferenceEquals(prop, target) || target.IsDescendantOf(prop))
            {
                Skip("props sent to bones not in the package");
                return;
            }
            var position = prop.GlobalPosition;
            var rotation = prop.GlobalRotation;
            prop.SetParent(target, preserveGlobalTransform: true);
            switch (d["attachmentMode"]?.Int() ?? 0)
            {
                case 2:
                    break;
                case 3:
                    prop.LocalPosition.Value = float3.Zero;
                    prop.GlobalRotation = rotation;
                    break;
                case 4:
                    prop.LocalRotation.Value = floatQ.Identity;
                    prop.GlobalPosition = position;
                    break;
                default:
                    prop.LocalPosition.Value = float3.Zero;
                    prop.LocalRotation.Value = floatQ.Identity;
                    break;
            }
            if ((d["matchScale"]?.Int() ?? 0) != 0)
                prop.LocalScale.Value = float3.One;
        }

        // Its bone's own mesh resized, the bones below it left as they were.
        private void ScaleAdjuster(UnityComponentRef part)
        {
            var bone = _nodes.Resolve(part.Node);
            var scale = Vector(part.Doc["m_Scale"]);
            if (bone == null || part.Doc["m_Scale"] == null || MathF.Abs(scale.x * scale.y * scale.z) < 1e-6f)
            {
                Skip("scale adjusters on bones not in the package");
                return;
            }
            var before = bone.LocalScale.Value;
            bone.LocalScale.Value = new float3(before.x * scale.x, before.y * scale.y, before.z * scale.z);
            foreach (var child in bone.Children)
            {
                var p = child.LocalPosition.Value;
                var s = child.LocalScale.Value;
                child.LocalPosition.Value = new float3(p.x / scale.x, p.y / scale.y, p.z / scale.z);
                child.LocalScale.Value = new float3(s.x / scale.x, s.y / scale.y, s.z / scale.z);
            }
        }

        private void WorldFixed(UnityComponentRef part)
        {
            if (_nodes.Resolve(part.Node) is not { } slot)
                return;
            var constraint = slot.GetComponent<TransformConstraint>() ?? slot.AttachComponent<TransformConstraint>();
            constraint.FreezeToWorld.Value = true;
        }

        // ---- menus and reactive switches ----

        private readonly Dictionary<UnityNodeRef, IField<bool>> _maSwitches = new();

        private void ModularSwitches(UnityBuildTools.Parts tools)
        {
            var parts = tools.ModularAvatar;
            if (parts.Count == 0)
                return;
            // Each menu item a switch, at the path the plan read it at: the plan's own where it answered the
            // item's parameter, else one of ours (items sharing a parameter take turns).
            var shared = tools.Items.Where(i => i.Parameter.Length > 0).GroupBy(i => i.Parameter).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var item in tools.Items)
            {
                if (item.Type is not ((int)UnityExpressionMenu.ControlType.Toggle or (int)UnityExpressionMenu.ControlType.Button))
                    continue;
                AvatarToggle toggle;
                if (item.Parameter.Length > 0 && _toggleByParameter.TryGetValue(item.Parameter, out var made) && !made.IsDestroyed)
                    toggle = made;
                else
                    toggle = NewToggle(item.Path, item.IsDefault, shared.Contains(item.Parameter) ? "ma:" + item.Parameter : "");
                if (item.Type == (int)UnityExpressionMenu.ControlType.Button)
                    HoldButton(toggle);
                _maSwitches[item.Part.Node] = toggle.State;
            }

            // Reactive components, deepest first so the deepest one's value is the one a shared field takes.
            var reactive = parts.Where(p => p.Kind is UnityBuildTools.Ma.ObjectToggle or UnityBuildTools.Ma.ShapeChanger)
                .OrderByDescending(p => Depth(p.Part.Node)).ToList();
            foreach (var (kind, part) in reactive)
            {
                var host = _nodes.Resolve(part.Node) ?? MenuSlot();
                var (always, when) = Condition(part, host);
                if (!always && when == null)
                    continue;
                if (kind == UnityBuildTools.Ma.ObjectToggle)
                    ReactiveObjects(part, when, host);
                else
                    ReactiveShapes(part, when, host);
            }
            foreach (var (kind, part) in parts)
                if (kind == UnityBuildTools.Ma.BlendshapeSync)
                    ShapeSync(part);
        }

        private static int Depth(UnityNodeRef node)
        {
            int depth = 0;
            for (var at = node.Parent; at != null && depth < 512; at = at.Parent)
                depth++;
            return depth;
        }

        // What a reactive component waits for: the nearest switch above it, turned around when it is inverted.
        // Always on with no switch above it; never with its object off, unless inverted.
        private (bool Always, IField<bool>? When) Condition(UnityComponentRef part, Slot host)
        {
            IField<bool>? sw = null;
            for (var at = part.Node; at != null && sw == null; at = at.Parent)
                _maSwitches.TryGetValue(at, out sw);
            bool inverted = (part.Doc["m_inverted"]?.Int() ?? 0) != 0;
            bool live = part.Enabled && (_nodes.Resolve(part.Node)?.IsActive ?? false);
            if (sw == null || !live)
                return (live != inverted, null);
            if (!inverted)
                return (false, sw);
            var holder = host.AttachComponent<ValueHolder<bool>>();
            var flip = host.AttachComponent<ValueSwitchDriver<bool>>();
            flip.OffValue.Value = true;
            flip.AddSwitch(sw, false);
            flip.AddDrive(holder.Value);
            return (false, holder.Value);
        }

        private void ReactiveObjects(UnityComponentRef part, IField<bool>? when, Slot host)
        {
            if (part.Doc["m_objects"] is not UnitySeq objects)
                return;
            foreach (var item in objects.Items)
            {
                if (AvatarObject(item["Object"]) is not { } target)
                {
                    Skip("reactive switches of objects not in the package");
                    continue;
                }
                bool wanted = (item["Active"]?.Int(1) ?? 1) != 0;
                bool rest = target.ActiveSelf.Value;
                if (when == null)
                    target.ActiveSelf.Value = wanted;
                else if (rest != wanted)
                    Switch(target.ActiveSelf, rest, wanted, when, host);
            }
        }

        private void ReactiveShapes(UnityComponentRef part, IField<bool>? when, Slot host)
        {
            if (part.Doc["m_shapes"] is not UnitySeq shapes)
                return;
            foreach (var item in shapes.Items)
            {
                var renderer = AvatarObject(item["Object"])?.GetComponent<SkinnedMeshRenderer>();
                var name = item["ShapeName"]?.Text ?? "";
                if (renderer == null || name.Length == 0)
                {
                    Skip("reactive shapes on meshes not in the package");
                    continue;
                }
                bool delete = (item["ChangeType"]?.Int() ?? 0) == 0;
                float weight = delete ? 1f : System.Math.Clamp((item["Value"]?.Float(100f) ?? 100f) / 100f, 0f, 1f);
                var driver = host.AttachComponent<BlendShapeSwitchDriver>();
                if (when != null)
                    driver.Switches.Add(when);
                else
                    driver.On.Value = true;
                driver.Renderer.Target = renderer;
                driver.BlendShape.Value = name;
                driver.Weight.Value = weight;
                driver.Priority.Value = SkinnedMeshRenderer.BlendShapePriorityToggle;
                _carried++;
            }
        }
    }
}
