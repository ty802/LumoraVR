// Copyright 2023-present Lyuma and contributors
// Copyright 2022-2023 lox9973
// Copyright (c) 2026 LUMORAVR LTD. Modifications: C# runtime sampling and sparse pose coverage.
// SPDX-License-Identifier: Apache-2.0
// Swing/twist and body-center conversion adapted from Unidot humanoid/transform_util.gd.
// See ThirdParty/Unidot-Apache-2.0.txt.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

public sealed class UnityHumanoidPose
{
    public readonly floatQ[] LocalRotations = new floatQ[55];
    public readonly floatQ[] GlobalRotations = new floatQ[55];
    public readonly float[] Weights = new float[55];
    public float3 HipsPosition;
    public float PositionWeight;
    public bool HasPose;
    private readonly float3[] _muscles = new float3[55];
    private readonly float3[] _positions = new float3[25];
    private readonly floatQ[] _rotations = new floatQ[25];
    private static readonly Dictionary<string, (int Bone, int Axis)> Channels = BuildChannels();
    public static readonly floatQ[] ReferenceGlobal = BuildGlobals(UnityHumanoidTables.Rest);

    private static Dictionary<string, (int, int)> BuildChannels()
    {
        var result = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        for (int bone = 0; bone < 55; bone++)
            for (int axis = 0; axis < 3; axis++)
            {
                int index = UnityHumanoidTables.MuscleFromBone[bone * 3 + axis];
                if (index < 0) continue;
                string name = UnityHumanoidTables.MuscleNames[index];
                result[name] = (bone, axis);
                if (name.Contains("Hand.")) result[name.Replace("Hand.", " ").Replace('.', ' ')] = (bone, axis);
            }
        return result;
    }

    public static bool IsMuscle(string name) => Channels.ContainsKey(name);
    public static string BoneName(int bone) => UnityHumanoidTables.BoneNames[bone];
    public static int Parent(int bone) => UnityHumanoidTables.Parents[bone];

    public void Sample(UnityAnimatorRuntime runtime)
    {
        Array.Clear(_muscles); Array.Clear(Weights);
        HasPose = false; PositionWeight = 0f;
        var rootT = new float3(0, 1, 0); var rootQ = floatQ.Identity;
        float rotationWeight = 0;
        var bindings = runtime.Graph.Bindings;
        foreach (int b in runtime.Graph.MuscleBindings)
        {
            var (value, weight, _) = runtime.Muscle(b);
            if (!float.IsFinite(value) || !float.IsFinite(weight) || weight <= 0) continue;
            string attribute = bindings[b].Key.Attribute;
            if (Channels.TryGetValue(attribute, out var channel))
            {
                var v = _muscles[channel.Bone];
                if (channel.Axis == 0) v.x = value; else if (channel.Axis == 1) v.y = value; else v.z = value;
                _muscles[channel.Bone] = v;
                Weights[channel.Bone] = MathF.Max(Weights[channel.Bone], weight);
                HasPose = true;
            }
            else if (attribute.StartsWith("RootT.", StringComparison.Ordinal))
            {
                if (attribute == "RootT.x") rootT.x = -value;
                if (attribute == "RootT.y") rootT.y = value;
                if (attribute == "RootT.z") rootT.z = value;
                PositionWeight = MathF.Max(PositionWeight, weight);
            }
            else if (attribute.StartsWith("RootQ.", StringComparison.Ordinal))
            {
                if (attribute == "RootQ.x") rootQ.x = value;
                if (attribute == "RootQ.y") rootQ.y = -value;
                if (attribute == "RootQ.z") rootQ.z = -value;
                if (attribute == "RootQ.w") rootQ.w = value;
                rotationWeight = MathF.Max(rotationWeight, weight);
            }
        }
        HasPose |= PositionWeight > 0 || rotationWeight > 0;
        for (int i = 0; i < 55; i++)
        {
            LocalRotations[i] = Weights[i] > 0 ? Rotation(i, _muscles[i]) : UnityHumanoidTables.Rest[i];
            int twistParent = i switch { 3 => 1, 4 => 2, 5 => 3, 6 => 4, 15 => 13, 16 => 14, 17 => 15, 18 => 16, _ => -1 };
            if (twistParent >= 0 && Weights[i] > 0)
            {
                var angles = _muscles[i]; angles.x *= .5f;
                LocalRotations[i] = Rotation(twistParent, new float3(_muscles[twistParent].x * .5f, 0, 0), true) * Rotation(i, angles);
            }
        }
        // RootT/RootQ refer to the body's center of mass, not the pelvis joint.
        if (HasPose && (PositionWeight > 0 || rotationWeight > 0))
        {
            _positions[0] = UnityHumanoidTables.ReferencePositions[0]; _rotations[0] = floatQ.Identity;
            for (int body = 1; body < 25; body++)
            {
                int parent = UnityHumanoidTables.BodyParents[body];
                _positions[body] = _positions[parent] + _rotations[parent] * UnityHumanoidTables.ReferencePositions[body];
                _rotations[body] = (_rotations[parent] * LocalRotations[UnityHumanoidTables.BodyOrder[body]]).Normalized;
            }
            var x = ((_positions[14] + _positions[1]) - (_positions[15] + _positions[2])).Normalized;
            var y = ((_positions[14] + _positions[15]) - (_positions[1] + _positions[2])).Normalized;
            var z = float3.Cross(x, y).Normalized; x = float3.Cross(y, z).Normalized;
            if (floatQ.Dot(rootQ, rootQ) < 1e-8f) rootQ = floatQ.Identity;
            var delta = (rootQ.Normalized * Basis(x, y, z).Inverse).Normalized;
            LocalRotations[0] = delta;
            Weights[0] = MathF.Max(rotationWeight, PositionWeight);
            var mass = float3.Zero; float total = 0;
            for (int i = 0; i < 25; i++)
            {
                float weight = UnityHumanoidTables.human_bone_mass[i];
                mass += (_positions[i] + _rotations[i] * new float3(0, UnityHumanoidTables.bone_lengths[i] * .5f, 0)) * weight;
                total += weight;
            }
            HipsPosition = rootT - delta * (mass / MathF.Max(total, 1e-6f) - _positions[0]);
        }
        // Parents are not numerically ordered (UpperChest is last).
        Array.Clear(_resolved);
        for (int i = 0; i < 55; i++) Resolve(i, 0);
    }

    private readonly bool[] _resolved = new bool[55];

    // Each bone's global turn once, parent first.
    private floatQ Resolve(int bone, int depth)
    {
        if (_resolved[bone])
            return GlobalRotations[bone];
        int parent = Parent(bone);
        var global = depth > 55 || parent < 0 ? LocalRotations[bone] : (Resolve(parent, depth + 1) * LocalRotations[bone]).Normalized;
        GlobalRotations[bone] = global;
        _resolved[bone] = true;
        return global;
    }

    private static floatQ Rotation(int bone, float3 values, bool fromPost = false)
    {
        var angles = float3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            int m = UnityHumanoidTables.MuscleFromBone[bone * 3 + axis];
            if (m < 0) continue;
            float value = axis == 0 ? values.x : axis == 1 ? values.y : values.z;
            var signs = UnityHumanoidTables.Signs[bone];
            float sign = axis == 0 ? signs.x : axis == 1 ? signs.y : signs.z;
            float angle = value * (value >= 0 ? UnityHumanoidTables.MuscleDefaultMax[m] : -UnityHumanoidTables.MuscleDefaultMin[m]) * sign * (MathF.PI / 180f);
            if (axis == 0) angles.x = angle; else if (axis == 1) angles.y = -angle; else angles.z = -angle;
        }
        var yz = new float3(0, angles.y, angles.z);
        var swing = yz.Length > 1e-8f ? floatQ.AxisAngle(yz.Normalized, yz.Length) : floatQ.Identity;
        var post = UnityHumanoidTables.PostInverse[bone].Normalized;
        var pre = fromPost ? post.Inverse : UnityHumanoidTables.Pre[bone].Normalized;
        return (pre * swing * floatQ.AxisAngle(float3.Right, angles.x) * post).Normalized;
    }

    public static floatQ Basis(float3 x, float3 y, float3 z)
    {
        var matrix = new System.Numerics.Matrix4x4(x.x, x.y, x.z, 0, y.x, y.y, y.z, 0, z.x, z.y, z.z, 0, 0, 0, 0, 1);
        var q = System.Numerics.Quaternion.CreateFromRotationMatrix(matrix);
        return new floatQ(q.X, q.Y, q.Z, q.W).Normalized;
    }
    private static floatQ Global(int bone, floatQ[] local, int depth)
        => depth > 55 || Parent(bone) < 0 ? local[bone] : (Global(Parent(bone), local, depth + 1) * local[bone]).Normalized;
    private static floatQ[] BuildGlobals(floatQ[] local)
    {
        var result = new floatQ[55];
        for (int i = 0; i < 55; i++) result[i] = Global(i, local, 0);
        return result;
    }
}
