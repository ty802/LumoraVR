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

// Constraints that hold a bone to a copy of the skeleton nothing here moves.
//
// Some avatars skin their body to one armature and pose it by copying a second, identical armature that an
// add-on we do not run solves every frame: each skinned bone is parented, at full weight, to its twin in the
// copy. Here the copy's bones never move against their parents, so every such constraint holds its bone at
// its bind pose against a parent that itself follows the copy. That is the same as no constraint at all, with
// the difference that a held bone cannot be posed by anything else: the body's own IK never reaches the legs
// and they hang off the hips at rest. These constraints are dropped.
//
// A constraint goes only when all of these hold, so anything with a job keeps it: a full parent copy at full
// weight with no switch or slider on its weights; every source it follows has no constraint, no driver and no
// jiggle on it, is not a rig bone and does not hang from one; the bone sits exactly on that pose now; and the
// bone's parent is the source's parent or follows it.
//
// A bone let go moves away from its twin, which stays behind, so anything hanging from that bone that still
// reads the copy would read a pose that no longer matches. A child copied whole from a twin that is itself
// turned by something outside the copy (a toe rolled from the foot) takes that twin's constraint over and
// hangs from its own parent again; for any other such reader the bones above it stay held as they were.
// Readers outside the bones let go see what they always saw. -xlinka
public static partial class UnityAvatarBuilder
{
    private sealed partial class FeatureBuilder
    {
        public void DropIdleConstraints()
        {
            var constraints = _model.GetComponentsInChildren<TransformConstraint>().ToList();
            if (constraints.Count == 0)
                return;
            var rigBones = new HashSet<Slot>();
            foreach (var rig in _model.GetComponentsInChildren<HumanoidRig>())
                foreach (var entry in rig.Bones)
                    if (entry.Value.Target is { IsDestroyed: false } bone)
                        rigBones.Add(bone);
            var jiggleRoots = new HashSet<Slot>();
            foreach (var chain in _model.GetComponentsInChildren<DynamicBoneChain>())
                if (chain.Root.Target is { } root)
                    jiggleRoots.Add(root);
            var constrained = new Dictionary<Slot, List<TransformConstraint>>();
            foreach (var c in constraints)
            {
                if (!constrained.TryGetValue(c.Slot, out var list))
                    constrained[c.Slot] = list = new List<TransformConstraint>();
                list.Add(c);
            }

            bool Idle(Slot source)
            {
                if (source.IsDestroyed || constrained.ContainsKey(source) || rigBones.Contains(source))
                    return false;
                if (source.LocalPosition.IsDriven || source.LocalRotation.IsDriven || source.LocalScale.IsDriven)
                    return false;
                for (var cursor = source; cursor != null && !ReferenceEquals(cursor, _model); cursor = cursor.Parent)
                {
                    if (rigBones.Contains(cursor) || jiggleRoots.Contains(cursor))
                        return false;
                }
                return true;
            }

            bool Follows(Slot? child, Slot? parent)
            {
                if (child == null || parent == null)
                    return false;
                if (ReferenceEquals(child, parent))
                    return true;
                if (!constrained.TryGetValue(child, out var list))
                    return false;
                return list.Any(c => c.Enabled.Value && c.Kind.Value == ConstraintKind.Parent
                    && c.Sources.Any(s => s.Weight.Value > 0f && ReferenceEquals(s.Source.Target, parent)));
            }

            var idle = new List<TransformConstraint>();
            foreach (var c in constraints)
            {
                if (!FullParentCopy(c) || c.Slot?.Parent == null)
                    continue;
                var target = c.Slot;
                var live = Live(c);
                if (live.Count == 0 || !live.All(s => Idle(s.Source.Target!)))
                    continue;
                if (!live.All(s => Follows(target.Parent, s.Source.Target!.Parent)))
                    continue;
                if (!HoldsInPlace(target, live))
                    continue;
                idle.Add(c);
            }

            // Undoing one drop can strand the bone above it in turn, so this runs until nothing changes.
            var carried = new List<(TransformConstraint Reader, Slot Twin)>();
            while (idle.Count > 0)
            {
                var leftBehind = new Dictionary<Slot, TransformConstraint>();
                var byTarget = new Dictionary<Slot, TransformConstraint>();
                foreach (var c in idle)
                {
                    byTarget[c.Slot] = c;
                    foreach (var s in Live(c))
                        leftBehind[s.Source.Target!] = c;
                }
                TransformConstraint? Stranding(Slot source)
                {
                    for (var cursor = source; cursor != null && !ReferenceEquals(cursor, _model); cursor = cursor.Parent)
                    {
                        if (leftBehind.TryGetValue(cursor, out var owner))
                            return owner;
                    }
                    return null;
                }
                TransformConstraint? LetGoAbove(Slot slot)
                {
                    for (var cursor = slot; cursor != null && !ReferenceEquals(cursor, _model); cursor = cursor.Parent)
                    {
                        if (byTarget.TryGetValue(cursor, out var drop))
                            return drop;
                    }
                    return null;
                }

                carried.Clear();
                TransformConstraint? undo = null;
                foreach (var c in constraints)
                {
                    if (c.IsDestroyed || (!c.Enabled.Value && !c.Enabled.IsDriven) || idle.Contains(c))
                        continue;
                    var above = LetGoAbove(c.Slot);
                    if (above == null)
                        continue;
                    bool stale = c.UpObject.Target is { IsDestroyed: false } up && Stranding(up) != null;
                    foreach (var s in c.Sources)
                    {
                        if (s.Source.Target is { IsDestroyed: false } source && (s.Weight.Value > 0f || s.Weight.IsDriven))
                            stale |= Stranding(source) != null;
                    }
                    if (!stale)
                        continue;
                    if (CarriedTwin(c, byTarget, leftBehind, constrained, Stranding) is { } twin)
                    {
                        carried.Add((c, twin));
                        continue;
                    }
                    undo = above;
                    break;
                }
                if (undo == null)
                    break;
                idle.Remove(undo);
            }

            // Decided before any goes, so a bone whose parent's constraint is also idle still reads as following.
            foreach (var (reader, twin) in carried)
            {
                foreach (var own in constrained[twin])
                    CarryOver(own, reader.Slot);
                reader.Destroy();
            }
            foreach (var c in idle)
                c.Destroy();
            if (idle.Count > 0)
                _result.Notes.Add($"Bones held to a copy of the skeleton only an add-on moved are left to the avatar's own IK: {idle.Count}"
                    + (carried.Count > 0 ? $", and {carried.Count} hanging from them keep the turn the copy gave them." : "."));
        }

        private static bool FullParentCopy(TransformConstraint c)
        {
            if (c.IsDestroyed || !c.Enabled.Value || c.Kind.Value != ConstraintKind.Parent || c.LocalSpace.Value
                || c.FreezeToWorld.Value || c.Weight.Value < 0.999f || c.Weight.IsDriven)
                return false;
            if (!(c.AffectPositionX.Value && c.AffectPositionY.Value && c.AffectPositionZ.Value
                  && c.AffectRotationX.Value && c.AffectRotationY.Value && c.AffectRotationZ.Value))
                return false;
            return !c.Sources.Any(s => s.Weight.IsDriven);
        }

        private static List<ConstraintSource> Live(TransformConstraint c)
            => c.Sources.Where(s => s.Weight.Value > 0f && s.Source.Target is { IsDestroyed: false }).ToList();

        // A bone copied whole from one twin that hangs off a twin left behind, where everything that moves the
        // twin only turns it and comes from outside the copy. The twin's local pose is the bone's at rest and
        // the parents matched, so the bone carrying the twin's own constraints does what the twin did.
        private static Slot? CarriedTwin(TransformConstraint c, Dictionary<Slot, TransformConstraint> byTarget,
            Dictionary<Slot, TransformConstraint> leftBehind, Dictionary<Slot, List<TransformConstraint>> constrained,
            Func<Slot, TransformConstraint?> stranding)
        {
            if (!FullParentCopy(c) || c.Slot?.Parent == null || !byTarget.TryGetValue(c.Slot.Parent, out var parentDrop))
                return null;
            var live = Live(c);
            if (live.Count != 1)
                return null;
            var entry = live[0];
            var twin = entry.Source.Target!;
            if (entry.PositionOffset.Value.Length > 1e-5f || MathF.Abs(entry.RotationOffset.Value.Normalized.w) < 0.99999f)
                return null;
            if (twin.Parent == null || !leftBehind.TryGetValue(twin.Parent, out var owner) || !ReferenceEquals(owner, parentDrop))
                return null;
            if (twin.LocalPosition.IsDriven || twin.LocalScale.IsDriven)
                return null;
            if (!constrained.TryGetValue(twin, out var turns))
                return null;
            foreach (var turn in turns)
            {
                if (turn.Kind.Value is not (ConstraintKind.Rotation or ConstraintKind.Aim or ConstraintKind.LookAt))
                    return null;
                if (turn.Enabled.IsDriven || turn.FreezeToWorld.Value || turn.Weight.IsDriven || turn.Sources.Any(t => t.Weight.IsDriven))
                    return null;
                if (turn.UpObject.Target is { IsDestroyed: false } up && stranding(up) != null)
                    return null;
                if (turn.Sources.Any(t => t.Source.Target is { IsDestroyed: false } from && t.Weight.Value > 0f && stranding(from) != null))
                    return null;
            }
            return HoldsInPlace(c.Slot, live) ? twin : null;
        }

        private static void CarryOver(TransformConstraint from, Slot onto)
        {
            var to = onto.AttachComponent<TransformConstraint>();
            to.Enabled.Value = from.Enabled.Value;
            to.Kind.Value = from.Kind.Value;
            to.Weight.Value = from.Weight.Value;
            to.LocalSpace.Value = from.LocalSpace.Value;
            to.RestPosition.Value = from.RestPosition.Value;
            to.RestRotation.Value = from.RestRotation.Value;
            to.RestScale.Value = from.RestScale.Value;
            to.PositionOffset.Value = from.PositionOffset.Value;
            to.RotationOffset.Value = from.RotationOffset.Value;
            to.ScaleOffset.Value = from.ScaleOffset.Value;
            to.AffectPositionX.Value = from.AffectPositionX.Value;
            to.AffectPositionY.Value = from.AffectPositionY.Value;
            to.AffectPositionZ.Value = from.AffectPositionZ.Value;
            to.AffectRotationX.Value = from.AffectRotationX.Value;
            to.AffectRotationY.Value = from.AffectRotationY.Value;
            to.AffectRotationZ.Value = from.AffectRotationZ.Value;
            to.AffectScaleX.Value = from.AffectScaleX.Value;
            to.AffectScaleY.Value = from.AffectScaleY.Value;
            to.AffectScaleZ.Value = from.AffectScaleZ.Value;
            to.AimAxis.Value = from.AimAxis.Value;
            to.UpAxis.Value = from.UpAxis.Value;
            to.UpMode.Value = from.UpMode.Value;
            to.WorldUp.Value = from.WorldUp.Value;
            to.UpObject.Target = from.UpObject.Target;
            to.Roll.Value = from.Roll.Value;
            foreach (var source in from.Sources)
            {
                var copy = to.AddSource(source.Source.Target!, source.Weight.Value);
                copy.PositionOffset.Value = source.PositionOffset.Value;
                copy.RotationOffset.Value = source.RotationOffset.Value;
            }
        }

        // The constraint's pose for its bone right now, against where the bone is: a constraint that holds a bone
        // where it already stands is one whose removal changes nothing at rest.
        private static bool HoldsInPlace(Slot target, List<ConstraintSource> sources)
        {
            float total = 0f;
            float3 position = float3.Zero;
            floatQ? first = null;
            var sum = new floatQ(0f, 0f, 0f, 0f);
            foreach (var entry in sources)
            {
                var source = entry.Source.Target!;
                float w = entry.Weight.Value;
                total += w;
                position += source.LocalPointToGlobal(entry.PositionOffset.Value) * w;
                var rotation = source.GlobalRotation * entry.RotationOffset.Value;
                first ??= rotation;
                float signed = floatQ.Dot(first.Value, rotation) < 0f ? -w : w;
                sum = new floatQ(sum.x + rotation.x * signed, sum.y + rotation.y * signed, sum.z + rotation.z * signed, sum.w + rotation.w * signed);
            }
            if (total < 1e-6f)
                return false;
            position /= total;
            float bone = float3.Distance(target.GlobalPosition, target.Parent.GlobalPosition);
            if (float3.Distance(position, target.GlobalPosition) > bone * 0.02f + 1e-4f)
                return false;
            return MathF.Abs(floatQ.Dot(sum.Normalized, target.GlobalRotation.Normalized)) > 0.9999f;
        }
    }
}
