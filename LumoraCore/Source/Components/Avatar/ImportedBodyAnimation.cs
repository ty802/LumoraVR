// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets.Interop.Unity;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// A pose source for AvatarIK, not a second writer fighting the solver. Its rest frames persist with
// the avatar, so reloading while posed cannot accidentally turn that pose into the retargeting bind.
[ComponentCategory("Users/Avatar")]
[DefaultUpdateOrder(900)]
public sealed class ImportedBodyAnimation : Component
{
    public readonly SyncRef<HumanoidRig> Rig = new();
    public readonly SyncRefList<ImportedAvatarAnimator> Sources = new();
    public readonly SyncFieldList<floatQ> RestRotations = new(), ParentRotations = new();
    public readonly SyncFieldList<int> ParentBones = new();
    public readonly Sync<float3> RestHips = new();
    public readonly Sync<floatQ> Orientation = new();
    public readonly Sync<float> HumanScale = new();
    public readonly Sync<bool> PreferImported = new();
    public readonly Sync<bool> PreserveTracking = new();
    public readonly record struct BonePose(floatQ Rotation, float Weight, float3? Position = null, float PositionWeight = 0);
    public readonly Dictionary<BodyNode, BonePose> Pose = new();
    private readonly Dictionary<ImportedAvatarAnimator, UnityHumanoidPose> _decoders = new();
    private readonly floatQ[] _desired = new floatQ[55];
    private static readonly BodyNode[] Nodes = BuildNodes();

    public override void OnInit()
    {
        base.OnInit(); PreferImported.Value = true; PreserveTracking.Value = true;
        Orientation.Value = floatQ.Identity; HumanScale.Value = 1;
    }

    private static BodyNode[] BuildNodes()
    {
        var meta = new UnityModelMeta();
        for (int i = 0; i < 55; i++) meta.HumanBones[UnityHumanoidPose.BoneName(i)] = i.ToString();
        var result = new BodyNode[55]; Array.Fill(result, BodyNode.NONE);
        foreach (var (node, index) in meta.HumanoidMap()) result[int.Parse(index)] = node;
        return result;
    }

    public void Capture(HumanoidRig rig)
    {
        Rig.Target = rig;
        RestRotations.Clear(); ParentRotations.Clear(); ParentBones.Clear();
        var root = Slot;
        for (int i = 0; i < 55; i++)
        {
            var bone = rig.TryGetBone(Nodes[i]);
            RestRotations.Add(bone == null ? floatQ.Identity : root.GlobalRotationToLocal(bone.GlobalRotation));
            ParentRotations.Add(bone?.Parent == null ? floatQ.Identity : root.GlobalRotationToLocal(bone.Parent.GlobalRotation));
            int parent = -1;
            for (var ancestor = bone?.Parent; ancestor != null && ancestor != root; ancestor = ancestor.Parent)
            {
                var node = rig.GetBoneType(ancestor);
                if (node != BodyNode.NONE && Array.IndexOf(Nodes, node) is int found && found >= 0) { parent = found; break; }
            }
            ParentBones.Add(parent);
        }
        var hips = rig.TryGetBone(BodyNode.Hips); var head = rig.TryGetBone(BodyNode.Head);
        var left = rig.TryGetBone(BodyNode.LeftUpperLeg); var right = rig.TryGetBone(BodyNode.RightUpperLeg);
        if (hips == null || head == null || left == null || right == null) return;
        RestHips.Value = root.GlobalPointToLocal(hips.GlobalPosition);
        var x = root.GlobalDirectionToLocal(left.GlobalPosition - right.GlobalPosition).Normalized;
        var y = root.GlobalDirectionToLocal(head.GlobalPosition - hips.GlobalPosition).Normalized;
        var z = float3.Cross(x, y).Normalized; x = float3.Cross(y, z).Normalized;
        Orientation.Value = UnityHumanoidPose.Basis(x, y, z);
        HumanScale.Value = MathF.Max(MathF.Abs(RestHips.Value.y), 1e-4f);
        var ik = root.GetComponent<AvatarIK>();
        if (ik != null) ik.BodyAnimation.Target = this;
    }

    public bool IsAvailable => Enabled.Value && Slot.IsActive && PreferImported.Value && Pose.Count > 0;
    // Tracking keeps the head and hands unless a state handed them to the animation (an emote).
    public bool KeepsTracking => PreserveTracking.Value && !_handedOver;
    private bool _handedOver;
    public bool ReplacesFeet => IsAvailable && Full(BodyNode.LeftUpperLeg) && Full(BodyNode.LeftLowerLeg)
        && Full(BodyNode.RightUpperLeg) && Full(BodyNode.RightLowerLeg);
    private bool Full(BodyNode node) => Pose.TryGetValue(node, out var pose) && pose.Weight >= .999f;

    public override void OnUpdate(float delta) => Sample();

    private static readonly floatQ[] ReferenceInverse = BuildReferenceInverse();
    private static floatQ[] BuildReferenceInverse()
    {
        var result = new floatQ[55];
        for (int i = 0; i < 55; i++) result[i] = UnityHumanoidPose.ReferenceGlobal[i].Inverse;
        return result;
    }

    // The saved frames and the rig's bones as plain arrays, read again when anything here changes.
    private readonly floatQ[] _rest = new floatQ[55], _parentRest = new floatQ[55];
    private readonly int[] _parents = new int[55];
    private readonly Slot?[] _bones = new Slot?[55];
    private bool _cached;

    public override void OnChanges() { base.OnChanges(); _cached = false; }

    private void Cache(HumanoidRig rig)
    {
        if (_cached)
            return;
        for (int i = 0; i < 55; i++)
        {
            _rest[i] = RestRotations[i];
            _parentRest[i] = ParentRotations[i];
            _parents[i] = ParentBones[i];
            _bones[i] = rig.TryGetBone(Nodes[i]);
        }
        _cached = true;
    }
    public void Sample()
    {
        Pose.Clear();
        var rig = Rig.Target;
        if (!Enabled.Value || !Slot.IsActive || !PreferImported.Value || rig == null || rig.IsDestroyed
            || RestRotations.Count != 55 || ParentRotations.Count != 55 || ParentBones.Count != 55) return;
        var orientation = Orientation.Value;
        _handedOver = false;
        foreach (var source in Sources)
        {
            if (source != null && !source.IsDestroyed)
                _handedOver |= source.TrackingMode(0) == 2 || source.TrackingMode(1) == 2 || source.TrackingMode(2) == 2;
            if (source == null || source.IsDestroyed || !source.Enabled.Value || !source.Slot.IsActive
                || source.Runtime is not { } runtime || runtime.Graph.MuscleBindings.Length == 0) continue;
            if (!_decoders.TryGetValue(source, out var decoder)) _decoders[source] = decoder = new UnityHumanoidPose();
            decoder.Sample(runtime);
            if (!decoder.HasPose) continue;
            Cache(rig);
            var orientationInverse = orientation.Inverse;
            for (int i = 0; i < 55; i++)
                _desired[i] = (orientation * decoder.GlobalRotations[i] * ReferenceInverse[i] * orientationInverse * _rest[i]).Normalized;
            for (int i = 0; i < 55; i++)
            {
                float weight = System.Math.Clamp(decoder.Weights[i], 0f, 1f);
                var bone = _bones[i];
                if (weight <= 0 || bone == null || bone.IsDestroyed) continue;
                int parent = _parents[i];
                var parentRotation = parent < 0 || parent >= 55 ? _parentRest[i]
                    : _desired[parent] * _rest[parent].Inverse * _parentRest[i];
                var rotation = (parentRotation.Inverse * _desired[i]).Normalized;
                float3? position = null;
                if (i == 0 && decoder.PositionWeight > 0)
                {
                    var local = RestHips.Value + orientation * (decoder.HipsPosition - new float3(0, 1, 0)) * HumanScale.Value;
                    var world = Slot.LocalPointToGlobal(local);
                    position = bone.Parent == null ? world : bone.Parent.GlobalPointToLocal(world);
                }
                if (Pose.TryGetValue(Nodes[i], out var before))
                {
                    float total = before.Weight * (1 - weight) + weight;
                    rotation = floatQ.Slerp(before.Rotation, rotation, weight / total);
                    weight = total;
                }
                Pose[Nodes[i]] = new(rotation, weight, position, decoder.PositionWeight);
            }
        }
    }
}
