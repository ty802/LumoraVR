// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Assets.Interop.Unity;

// PhysBone settings put onto our chain by what they mean, on the same 60 Hz scale as Dynamic Bone:
//
//   pull       the share of the way back to rest made each frame, so elasticity is pull x 60 squared.
//   spring     (momentum) the share of the bones' own motion kept each frame, so damping is its decay rate.
//   stiffness  how hard a bone keeps its rest place: the chain's clamp. Only the advanced kind has it.
//   gravity    in the squishy version a share of the angle toward the ground a bone hangs at rest, which is
//              our Sag; the falloff takes it away while the bone sits at rest. The first version's gravity
//              is a force.
//   immobile   how much of the body's motion the bones simply ride, turns included: Inertia.
//   limits     cone, hinge or polar about each bone's rest direction under its parent.
//   animated   bones take the pose their animation gives them as their rest each frame (FollowAnimation).
//   curves     every value scaled along the chain, root to tip.
//   collision  with other people's hands, the wearer's own hands, both or neither; the head never counts.
//   immobile   of the World kind rides only the body's travel through the world, not its own motion.
//   children   a root with several turns toward none of them, its first, or the middle of them all.
//   outputs    a chain that names a parameter reports its grab, angle, stretch and squish for the animation.
//   stretch    how much longer a bone may get, as a share of its length; squish how much shorter (0..1); stretch
//              motion how much the bones' own motion may change it, past what a hand or a collider does. The first
//              version has no squish and no motion stretch: only a hand draws its bones out.
//
// Bones move with the body rather than through the world (RelativeMotion): the settings are tuned for bones
// that ride steady walking and turning and only swing when the body speeds up, stops or turns. A root with
// more than one child holds still and only what hangs from it moves, which is also how a head carrying both
// ears is set up; a root two chains share holds still for both, or each would keep taking the other's
// swing as its rest. -xlinka
public static partial class UnityAvatarBuilder
{
    private static void ApplyPhysBone(DynamicBoneChain chain, UnityDocument doc, float unit, bool rootHolds)
    {
        bool advanced = (doc["integrationType"]?.Int() ?? 0) == 1;
        bool squishy = (doc["version"]?.Int() ?? 0) >= 1;

        chain.RelativeMotion.Value = true;
        chain.InertiaForce.Value = 0f;
        chain.SwingRoot.Value = !rootHolds;

        float pull = Clamp01(doc["pull"]?.Float(0.2f) ?? 0.2f);
        chain.Elasticity.Value = pull * 3600f;
        Profile(chain.ElasticityCurve, doc["pullCurve"], m => m);

        float spring = Clamp01(doc["spring"]?.Float(0.2f) ?? 0.2f);
        float rate = MathF.Max(Decay(spring), 0.01f);
        chain.Damping.Value = rate;
        Profile(chain.DampingCurve, doc["springCurve"], m => Decay(spring * m) / rate);

        chain.Stiffness.Value = advanced ? Clamp01(doc["stiffness"]?.Float(0.2f) ?? 0.2f) : 0f;
        if (advanced)
            Profile(chain.StiffnessCurve, doc["stiffnessCurve"], m => m);

        float gravity = doc["gravity"]?.Float() ?? 0f;
        if (squishy)
        {
            chain.Gravity.Value = float3.Zero;
            chain.Sag.Value = System.Math.Clamp(gravity, -1f, 1f);
            chain.SagFalloff.Value = Clamp01(doc["gravityFalloff"]?.Float() ?? 0f);
        }
        else
        {
            chain.Gravity.Value = new float3(0f, -gravity * 9.81f, 0f);
        }
        Profile(chain.GravityCurve, doc["gravityCurve"], m => m);

        chain.Inertia.Value = Clamp01(doc["immobile"]?.Float() ?? 0f);
        Profile(chain.InertiaCurve, doc["immobileCurve"], m => m);

        chain.BaseBoneRadius.Value = (doc["radius"]?.Float() ?? 0f) / unit;
        Profile(chain.RadiusCurve, doc["radiusCurve"], m => m);

        var end = Vector(doc["endpointPosition"]);
        chain.SimulateTerminalBones.Value = end.LengthSquared > 1e-10f;
        chain.EndOffset.Value = Mirror(end) / unit;

        chain.Limit.Value = (doc["limitType"]?.Int() ?? 0) switch
        {
            1 => DynamicBoneLimit.Cone,
            2 => DynamicBoneLimit.Hinge,
            3 => DynamicBoneLimit.Polar,
            _ => DynamicBoneLimit.None,
        };
        if (chain.Limit.Value != DynamicBoneLimit.None)
        {
            chain.LimitAngle.Value = MathF.Max(doc["maxAngleX"]?.Float(45f) ?? 45f, 0f);
            chain.LimitAngleSide.Value = MathF.Max(doc["maxAngleZ"]?.Float(45f) ?? 45f, 0f);
            chain.LimitRotation.Value = LimitTurn(doc["limitRotation"]);
            Profile(chain.LimitCurve, doc["maxAngleXCurve"], m => m);
        }

        float stretch = doc["maxStretch"]?.Float() ?? 0f;
        chain.MaxStretch.Value = float.IsFinite(stretch) ? System.Math.Clamp(stretch, 0f, MaxStretchShare) : (stretch > 0f ? MaxStretchShare : 0f);
        Profile(chain.StretchCurve, doc["maxStretchCurve"], m => m);
        if (squishy)
        {
            chain.MaxSquish.Value = Clamp01(doc["maxSquish"]?.Float() ?? 0f);
            Profile(chain.SquishCurve, doc["maxSquishCurve"], m => m);
            chain.StretchMotion.Value = Clamp01(doc["stretchMotion"]?.Float() ?? 0f);
            Profile(chain.StretchMotionCurve, doc["stretchMotionCurve"], m => m);
        }
        else
        {
            chain.MaxSquish.Value = 0f;
            chain.SquishCurve.Clear();
            chain.StretchMotion.Value = 0f;
            chain.StretchMotionCurve.Clear();
        }

        chain.FollowAnimation.Value = (doc["isAnimated"]?.Int() ?? 0) != 0;
        chain.AllowGrab.Value = (doc["allowGrabbing"]?.Int(1) ?? 1) != 0;
        chain.GrabFollow.Value = Clamp01(doc["grabMovement"]?.Float(0.5f) ?? 0.5f);
        chain.GrabSnapToHand.Value = (doc["snapToHand"]?.Int() ?? 0) != 0;

        // Who touches it: 0 nobody, 1 everyone, 2 as the filter says. "Everyone" is other people's hands and the
        // wearer's own; heads are not part of it.
        int collision = doc["allowCollision"]?.Int(1) ?? 1;
        var filter = doc["collisionFilter"];
        chain.CollideWithPlayers.Value = collision == 1 || collision == 2 && (filter?["allowOthers"]?.Int(1) ?? 1) != 0;
        chain.CollideWithOwnHands.Value = collision == 1 || collision == 2 && (filter?["allowSelf"]?.Int(1) ?? 1) != 0;
        chain.CollideWithOwnBody.Value = false;

        chain.InertiaTravelOnly.Value = (doc["immobileType"]?.Int() ?? 0) == 1;
        chain.RootFork.Value = (doc["multiChildType"]?.Int() ?? 0) switch
        {
            1 => DynamicBoneFork.First,
            2 => DynamicBoneFork.Average,
            _ => DynamicBoneFork.Hold,
        };
    }

    // An unbounded stretch is held to a hundred times a bone's length.
    private const float MaxStretchShare = 100f;

    // The rate a share kept per 60 Hz frame decays at. Nothing kept at all is capped, as a frame of decay.
    private static float Decay(float kept) => -MathF.Log(System.Math.Clamp(kept, 0.02f, 1f)) * 60f;

    private static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);

    // A curve along the chain, sampled at even steps through its own keys and mapped to our multiplier.
    // Flat at 1 leaves the list empty.
    private static void Profile(SyncFieldList<float2> into, UnityNode? source, Func<float, float> map)
    {
        into.Clear();
        if (source?["m_Curve"] is not UnitySeq keys || keys.Items.Count == 0)
            return;
        var curve = new UnityAnimationClip.FloatCurve();
        foreach (var key in keys.Items)
            curve.Keys.Add(new UnityAnimationClip.Key(key["time"]?.Float() ?? 0f, key["value"]?.Float() ?? 0f,
                key["inSlope"]?.Float() ?? 0f, key["outSlope"]?.Float() ?? 0f));
        curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));

        const int Steps = 8;
        var points = new List<float2>(Steps + 1);
        bool flat = true;
        for (int i = 0; i <= Steps; i++)
        {
            float t = i / (float)Steps;
            float value = map(curve.Sample(t));
            if (!float.IsFinite(value))
                value = 1f;
            flat &= MathF.Abs(value - 1f) < 1e-4f;
            points.Add(new float2(t, value));
        }
        if (flat)
            return;
        foreach (var point in points)
            into.Add(point);
    }

    // Pitch, yaw and roll in degrees, turned about Z, then X, then Y, then mirrored into ours.
    private static floatQ LimitTurn(UnityNode? node)
    {
        if (node == null)
            return floatQ.Identity;
        var e = Vector(node) * (MathF.PI / 180f);
        var q = floatQ.AxisAngle(new float3(0f, 1f, 0f), e.y)
            * floatQ.AxisAngle(new float3(1f, 0f, 0f), e.x)
            * floatQ.AxisAngle(new float3(0f, 0f, 1f), e.z);
        return MirrorRotation(q);
    }

    // Which chain roots hold still: one with more than one child of its own, before anything is ignored or
    // our colliders are hung on it, and one more than one chain shares.
    private static HashSet<Slot> HeldRoots(UnityImportCandidate candidate, NodeIndex nodes)
    {
        var uses = new Dictionary<Slot, int>();
        var held = new HashSet<Slot>();
        foreach (var part in candidate.Chains)
        {
            var root = ChainRoot(candidate, part, nodes);
            if (root == null)
                continue;
            uses[root] = uses.TryGetValue(root, out var count) ? count + 1 : 1;
            // Only the kind that ignores its root holds it; the others turn it toward one child or all of them.
            if (part.Type == UnityComponentRef.Kind.PhysBone && root.ChildCount > 1 && (part.Doc["multiChildType"]?.Int() ?? 0) == 0)
                held.Add(root);
        }
        foreach (var (root, count) in uses)
            if (count > 1)
                held.Add(root);
        return held;
    }

    // A PhysBone collider that keeps bones inside it: the sphere or capsule it would be, turned inside out. A plane
    // has no inside and is left out.
    private static IDynamicBoneCollider? BuildInside(UnityImportCandidate candidate, UnityComponentRef part, NodeIndex nodes, float unit)
    {
        var doc = part.Doc;
        int shape = doc["shapeType"]?.Int() ?? 0;
        if (shape == 2)
            return null;
        long rootId = doc["rootTransform"]?.Ref.FileId ?? 0;
        var at = rootId != 0 ? candidate.ResolveTransform(rootId) : part.Node;
        var host = at != null ? nodes.Resolve(at) : null;
        float radius = doc["radius"]?.Float() ?? 0f;
        if (host == null || radius <= 0f)
            return null;
        float height = shape == 1 ? doc["height"]?.Float() ?? 0f : 0f;
        var offset = Mirror(Vector(doc["position"])) / unit;
        var rotation = doc["rotation"] is { } r
            ? MirrorRotation(new floatQ(r["x"]?.Float() ?? 0f, r["y"]?.Float() ?? 0f, r["z"]?.Float() ?? 0f, r["w"]?.Float(1f) ?? 1f))
            : floatQ.Identity;
        if (height > radius * 2f)
        {
            var target = host;
            if (!IsIdentity(rotation))
            {
                target = host.AddSlot("Collider");
                target.LocalPosition.Value = offset;
                target.LocalRotation.Value = rotation;
                offset = float3.Zero;
            }
            var capsule = target.AttachComponent<DynamicBoneCapsuleCollider>();
            capsule.Radius.Value = radius / unit;
            capsule.Height.Value = height / unit;
            capsule.Offset.Value = offset;
            capsule.Inside.Value = true;
            return capsule;
        }
        var sphere = host.AttachComponent<DynamicBoneSphereCollider>();
        sphere.Radius.Value = radius / unit;
        sphere.Offset.Value = offset;
        sphere.Inside.Value = true;
        return sphere;
    }

    // A PhysBone collider meets each bone along its length unless it is set to meet the joints alone.
    private static void CollideSegments(IDynamicBoneCollider? collider, UnityComponentRef? source)
    {
        if (source?.Type != UnityComponentRef.Kind.PhysBoneCollider || (source.Doc["bonesAsSpheres"]?.Int() ?? 0) != 0)
            return;
        switch (collider)
        {
            case DynamicBoneSphereCollider sphere when !sphere.Inside.Value:
                sphere.Segments.Value = true;
                break;
            case DynamicBoneCapsuleCollider capsule when !capsule.Inside.Value:
                capsule.Segments.Value = true;
                break;
        }
    }

    private static Slot? ChainRoot(UnityImportCandidate candidate, UnityComponentRef part, NodeIndex nodes)
    {
        var doc = part.Doc;
        long rootId = (part.Type == UnityComponentRef.Kind.PhysBone ? doc["rootTransform"] : doc["m_Root"])?.Ref.FileId ?? 0;
        var rootRef = rootId != 0 ? candidate.ResolveTransform(rootId) : part.Node;
        return rootRef != null ? nodes.Resolve(rootRef) : null;
    }
}
