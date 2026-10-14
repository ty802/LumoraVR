// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// The body of the stand-in: a head, a torso, two arms ending in hands, two legs and a pool of light on the
// floor, each one of the kit's two shapes stretched by its slot. Head and hands sit where the person's
// tracked head and hands are; the rest is worked out from those and the floor, the way UserRoot estimates
// a body nobody tracks, so a nod, a wave or a step reads at once. Proportions scale with the user.
//
// Only what moved is written: each piece keeps the local transform it last had and a pose that puts it
// back where it is costs a compare. The head goes on a slot of its own so that on your own body it can ride
// the band your eyes skip while your arms and legs stay in view below you. -xlinka
internal sealed class AvatarArrivalFigure
{
    private sealed class Piece
    {
        public Slot Slot = null!;
        public MeshRenderer Renderer = null!;
        public bool Pad;
        public float3 Position;
        public floatQ Rotation;
        public float3 Scale;
        public bool Placed;
    }

    // The band ordinary geometry draws on, seen by every eye and camera.
    private const int PublicLayer = 1;

    private const float MoveEpsilon = 0.0004f;
    private const float TurnEpsilon = 0.99999f;

    private readonly Piece _head, _torso, _leftArm, _rightArm, _leftHand, _rightHand, _leftLeg, _rightLeg, _pad;
    private readonly Piece[] _pieces;
    private readonly RenderLayerOverride _crownBand;
    private float4 _painted = new(float.NaN, 0f, 0f, 0f);

    public Slot Root { get; }

    public AvatarArrivalFigure(Slot parent, AvatarArrivalKit kit)
    {
        Root = parent.AddSlot("Figure");
        var body = Root.AddSlot("Body");
        var crown = Root.AddSlot("Crown");
        _crownBand = crown.AttachComponent<RenderLayerOverride>();
        _crownBand.Layer.Value = PublicLayer;

        _head = Make(crown, "Head", kit.Ball, kit);
        _torso = Make(body, "Torso", kit.Capsule, kit);
        _leftArm = Make(body, "Left Arm", kit.Capsule, kit);
        _rightArm = Make(body, "Right Arm", kit.Capsule, kit);
        _leftHand = Make(body, "Left Hand", kit.Ball, kit);
        _rightHand = Make(body, "Right Hand", kit.Ball, kit);
        _leftLeg = Make(body, "Left Leg", kit.Capsule, kit);
        _rightLeg = Make(body, "Right Leg", kit.Capsule, kit);
        _pad = Make(body, "Pool", kit.Ball, kit);
        _pad.Pad = true;
        _pieces = new[] { _head, _torso, _leftArm, _rightArm, _leftHand, _rightHand, _leftLeg, _rightLeg, _pad };
    }

    private static Piece Make(Slot parent, string name, Meshes.ProceduralMesh? mesh, AvatarArrivalKit kit)
    {
        var slot = parent.AddSlot(name);
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh!;
        if (kit.Material != null)
            renderer.Material.Target = kit.Material;
        // Light, not a body: it casts nothing, and a crowd arriving costs no shadow passes.
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        return new Piece { Slot = slot, Renderer = renderer };
    }

    // On your own body the head draws for mirrors and cameras only, like your own name plate.
    public void SetOwnView(bool ownEyes)
    {
        int band = ownEyes ? RenderLayerOverride.OwnNameplateLayer : PublicLayer;
        if (!_crownBand.IsDestroyed && _crownBand.Layer.Value != band)
            _crownBand.Layer.Value = band;
    }

    // Presence, the floor and the top of the figure in world height, progress, and whether it failed: the
    // shader's per-piece value (AvatarArrival.gdshader). Written to every piece only when it moved.
    public void Paint(float presence, float floorY, float topY, float progress, bool failed)
    {
        var value = new float4(presence, floorY, topY, System.Math.Clamp(progress, 0f, 1f) + (failed ? 2f : 0f));
        if (value == _painted)
            return;
        _painted = value;
        foreach (var piece in _pieces)
        {
            if (piece.Renderer.IsDestroyed)
                continue;
            var own = piece.Pad ? new float4(-value.x, value.y, value.z, value.w) : value;
            piece.Renderer.SetInstanceParams(own);
        }
    }

    // Lays the figure over the person, in world space. Returns the floor and the top of the figure for Paint.
    public void Pose(UserRoot user, out float floorY, out float topY)
    {
        var rootSlot = user.Slot;
        float s = System.Math.Clamp(user.GlobalScale, 0.05f, 50f);
        var head = user.HeadSlot;
        var headPos = head?.GlobalPosition ?? rootSlot.GlobalPosition + float3.Up * 1.6f * s;
        var headRot = head?.GlobalRotation ?? rootSlot.GlobalRotation;
        var up = (rootSlot.GlobalRotation * float3.Up).Normalized;
        var level = user.LevelHeadRotation;
        var forward = level * float3.Backward;
        var right = level * float3.Right;
        var floor = user.GroundProjectedHeadPosition;

        float stand = System.Math.Max(float3.Dot(headPos - floor, up), 0.35f * s);
        floorY = floor.y;
        topY = floor.y + (stand + 0.13f * s) * up.y;

        var neck = headPos - up * (0.15f * s) + forward * (-0.035f * s);
        var hips = floor + up * (stand * 0.56f) - forward * (0.05f * s);
        if (float3.Dot(neck - hips, up) < 0.18f * s)
            hips = neck - up * (0.18f * s);
        var shoulderSpan = right * (0.165f * s);
        var shoulders = neck - up * (0.035f * s);
        var leftShoulder = shoulders - shoulderSpan;
        var rightShoulder = shoulders + shoulderSpan;

        var leftHand = HandOr(user.LeftHandSlot, headPos, leftShoulder, up, forward, s);
        var rightHand = HandOr(user.RightHandSlot, headPos, rightShoulder, up, forward, s);

        Place(_head, headPos + headRot * (new float3(0f, 0.025f, 0.055f) * s), headRot, new float3(0.2f, 0.245f, 0.23f) * s);
        Limb(_torso, hips, neck, level, 0.30f * s, 0.19f * s, 0.16f * s);
        Limb(_leftArm, leftShoulder, leftHand.Position, level, 0.085f * s, 0.085f * s, 0.06f * s);
        Limb(_rightArm, rightShoulder, rightHand.Position, level, 0.085f * s, 0.085f * s, 0.06f * s);
        Place(_leftHand, leftHand.Position, leftHand.Rotation, new float3(0.08f, 0.045f, 0.11f) * s);
        Place(_rightHand, rightHand.Position, rightHand.Rotation, new float3(0.08f, 0.045f, 0.11f) * s);

        var hipSpan = right * (0.095f * s);
        var footSpan = right * (0.11f * s);
        var feet = floor + up * (0.05f * s) - forward * (0.02f * s);
        Limb(_leftLeg, hips - hipSpan, feet - footSpan, level, 0.13f * s, 0.13f * s, 0.08f * s);
        Limb(_rightLeg, hips + hipSpan, feet + footSpan, level, 0.13f * s, 0.13f * s, 0.08f * s);

        // The ball pressed flat: its top faces up whichever way round its triangles are wound.
        Place(_pad, floor + up * (0.01f * s), level, new float3(0.95f * s, 0.012f * s, 0.95f * s));
    }

    // A tracked hand where it is; a hand the head has lost track of (or never had) hangs at the side.
    private static (float3 Position, floatQ Rotation) HandOr(Slot? hand, float3 headPos, float3 shoulder, float3 up,
        float3 forward, float s)
    {
        if (hand != null && !hand.IsDestroyed && hand.IsActive)
        {
            var at = hand.GlobalPosition;
            if ((at - headPos).LengthSquared < 1.4f * 1.4f * s * s)
                return (at, hand.GlobalRotation);
        }
        var rest = shoulder - up * (0.55f * s) + forward * (0.06f * s);
        return (rest, floatQ.Identity);
    }

    // A capsule from a to b: width and depth across it, overlap past each end so joints meet.
    private void Limb(Piece piece, float3 a, float3 b, floatQ frame, float width, float depth, float overlap)
    {
        var along = b - a;
        float length = along.Length;
        var direction = length > 1e-4f ? along / length : frame * float3.Up;
        var rotation = (FromTo(frame * float3.Up, direction) * frame).Normalized;
        // The unit capsule is two long on Y.
        Place(piece, (a + b) * 0.5f, rotation, new float3(width, (length + overlap) * 0.5f, depth));
    }

    private void Place(Piece piece, float3 position, floatQ rotation, float3 scale)
    {
        var slot = piece.Slot;
        if (slot.IsDestroyed)
            return;
        var parent = slot.Parent;
        if (parent == null)
            return;
        var local = parent.GlobalPointToLocal(position);
        var localRotation = parent.GlobalRotationToLocal(rotation);
        var localScale = parent.GlobalScaleToLocal(scale);

        if (!piece.Placed || (local - piece.Position).LengthSquared > MoveEpsilon * MoveEpsilon)
        {
            piece.Position = local;
            slot.LocalPosition.Value = local;
        }
        if (!piece.Placed || MathF.Abs(floatQ.Dot(localRotation, piece.Rotation)) < TurnEpsilon)
        {
            piece.Rotation = localRotation;
            slot.LocalRotation.Value = localRotation;
        }
        if (!piece.Placed || (localScale - piece.Scale).LengthSquared > MoveEpsilon * MoveEpsilon)
        {
            piece.Scale = localScale;
            slot.LocalScale.Value = localScale;
        }
        piece.Placed = true;
    }

    private static floatQ FromTo(float3 from, float3 to)
    {
        float d = float3.Dot(from, to);
        if (d > 0.99999f)
            return floatQ.Identity;
        if (d < -0.99999f)
        {
            var axis = float3.Cross(float3.Right, from);
            if (axis.LengthSquared < 1e-6f)
                axis = float3.Cross(float3.Forward, from);
            return floatQ.AxisAngle(axis.Normalized, MathF.PI);
        }
        var cross = float3.Cross(from, to);
        float w = MathF.Sqrt((1f + d) * 2f);
        float inv = 1f / w;
        return new floatQ(cross.x * inv, cross.y * inv, cross.z * inv, w * 0.5f).Normalized;
    }
}
