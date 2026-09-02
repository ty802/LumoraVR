// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Turns a user's tracked head and hands into dynamic-bone colliders, so brushing someone's tail
// actually moves it. Auto-added to the avatar scaffold next to the character collider.
//
// LOCAL COMPUTATION, NO EXTRA SYNC. Every peer builds these shapes for every user out of the
// tracking that is already replicated (that is how the hands are drawn in the first place), and runs
// them against the chains it is already simulating locally. Nothing about a collision is sent
// anywhere. Two peers can disagree by a frame of jitter about where a tail ended up, which is the
// same deal the rest of the bone simulation has always had.
//
// WHERE THE PRIVACY TOGGLE LIVES. AffectOthersBones is a replicated field on this component, NOT an
// EngineSettings entry, and that is forced rather than chosen: the peers that must honour "keep my
// body out of other people's hair" are the OTHER peers, and they cannot read your machine-local
// settings file. A local-only preference here would be a placebo that stops nothing. The field rides
// the user's own avatar scaffold, so it is on the wire already and every peer checks it before
// contributing a single shape. -xlinka
[ComponentCategory("Physics/Dynamic Bones")]
public class DynamicBonePlayerColliders : UserRootComponent
{
    public readonly Sync<bool> HeadCollider = new();
    public readonly Sync<bool> HandColliders = new();

    // Radii in metres at 1:1 user scale; the user root's global scale multiplies them, so a shrunk
    // user's hands stop pushing bones around like a giant's.
    public readonly Sync<float> HeadRadius = new();
    public readonly Sync<float> HandRadius = new();

    // How far forward of the wrist the hand capsule reaches. One capsule covers palm and fingers,
    // which is what a row of spheres would have cost otherwise.
    public readonly Sync<float> HandLength = new();

    // The privacy toggle. False and this user contributes nothing anywhere - their body passes
    // through everyone's hair and their own.
    public readonly Sync<bool> AffectOthersBones = new();

    private DynamicBoneManager? _manager;

    // Body-node slots are cached, not asked for each frame. The lookup behind HeadSlot / LeftHandSlot
    // builds a predicate to search the user's component registry, and that predicate is garbage this
    // pass must not be making sixty times a second per user in the room. Re-resolved when the cached
    // root changes (respawn, avatar swap) or a cached slot dies, and rate limited so a user who has
    // no hands yet does not pay for the search every frame. -xlinka
    private const double ResolveInterval = 1.0;
    private UserRoot? _cachedRoot;
    private Slot? _head;
    private Slot? _leftHand;
    private Slot? _rightHand;
    private double _nextResolve = double.NegativeInfinity;

    public override void OnInit()
    {
        base.OnInit();
        HeadCollider.Value = true;
        HandColliders.Value = true;
        HeadRadius.Value = 0.16f;
        HandRadius.Value = 0.05f;
        HandLength.Value = 0.11f;
        AffectOthersBones.Value = true;
    }

    public override void OnStart()
    {
        base.OnStart();
        _manager = DynamicBoneManager.For(World);
        _manager?.Register(this);
    }

    public override void OnDestroy()
    {
        _manager?.Unregister(this);
        _manager = null;
        base.OnDestroy();
    }

    // Called once per frame by the manager during the world's collider gather.
    internal void Contribute(DynamicBoneManager manager)
    {
        if (!Enabled.Value || IsDestroyed || !AffectOthersBones.Value)
            return;

        var root = Slot?.ActiveUserRoot;
        if (root == null || root.IsDestroyed || root.Slot == null || !root.Slot.IsActive)
            return;

        ResolveBodyNodes(root);

        var owner = root.ActiveUser;
        float scale = root.GlobalScale;
        if (scale <= 1e-4f)
            scale = 1f;

        if (HeadCollider.Value && _head != null && !_head.IsDestroyed)
        {
            // Pulled back from the eyes toward the middle of the skull: the head slot sits at the view
            // point, and a sphere centred there pushes hair off the face and leaves the back of the
            // head hollow.
            float3 centre = _head.LocalPointToGlobal(float3.Backward * 0.1f);
            manager.AddPlayerCollider(centre, centre, HeadRadius.Value * scale, owner);
        }

        if (HandColliders.Value)
        {
            AddHand(manager, _leftHand, scale, owner);
            AddHand(manager, _rightHand, scale, owner);
        }
    }

    private void ResolveBodyNodes(UserRoot root)
    {
        bool stale = !ReferenceEquals(root, _cachedRoot)
            || (_head == null || _head.IsDestroyed)
            || (_leftHand == null || _leftHand.IsDestroyed)
            || (_rightHand == null || _rightHand.IsDestroyed);
        if (!stale)
            return;

        double now = World?.Time.TotalTime ?? 0d;
        if (ReferenceEquals(root, _cachedRoot) && now < _nextResolve)
            return;
        _nextResolve = now + ResolveInterval;

        _cachedRoot = root;
        _head = root.HeadSlot;
        _leftHand = root.LeftHandSlot;
        _rightHand = root.RightHandSlot;
    }

    private void AddHand(DynamicBoneManager manager, Slot? hand, float scale, User? owner)
    {
        if (hand == null || hand.IsDestroyed)
            return;
        float3 wrist = hand.GlobalPosition;
        float3 tip = hand.LocalPointToGlobal(new float3(0f, 0f, HandLength.Value));
        manager.AddPlayerCollider(wrist, tip, HandRadius.Value * scale, owner);
    }
}
