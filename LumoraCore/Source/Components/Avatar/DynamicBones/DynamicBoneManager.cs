// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;
using Logger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// One per world: every chain in that world simulates through here instead of doing its own thing in
// its own input callback.
//
// What batching actually buys, in order of size:
//   - Player colliders are gathered ONCE per frame for the whole world instead of once per chain. A
//     room with twenty tails no longer walks twenty user hierarchies for the same six spheres.
//   - The local user's head position (view-distance pause) and the frame clock are resolved once.
//   - A single time measurement covers all chains, so the budget guard has something real to report
//     instead of a per-chain number that never looks expensive on its own.
//
// WHY CHAINS STILL REGISTER THEMSELVES AS INPUT RECEIVERS instead of this class registering one:
// the input dispatch sorts receivers by COMPONENT update order and a non-component sorts as 0, which
// lands after particle systems (-3000). Particles deliberately run after bones so an emitter parented
// to a tail sees the solved pose in the same frame. So each chain keeps its own dispatch slot (its
// own UpdateOrder, -4000 by default) and the first one to tick flushes every chain at or before its
// order. With the default order that is one pass for the whole world; a chain someone pushed to a
// later order still gets its own later pass, which is what asking for a later order meant. -xlinka
public sealed class DynamicBoneManager
{
    private static readonly ConditionalWeakTable<World, DynamicBoneManager> Managers = new();

    // Per-frame cost above this logs once and then never again for this world. It is a report, not a
    // cut: dropping chains mid-frame would be visible as half the room's hair freezing, which is worse
    // than the frame it saves. The number is a whole 60Hz frame's worth of slack for secondary motion.
    private const double BudgetMilliseconds = 2.0;

    // Consecutive over-budget frames before the report fires, and how long a world gets to settle
    // first. One frame proves nothing: the first pass pays for jitting this code, and a world that
    // just came up is loading everything it owns, so its early frames are slow for reasons that have
    // nothing to do with hair. Three bad frames after two seconds of running is a cost. -xlinka
    private const int BudgetStrikes = 3;
    private const ulong BudgetWarmupFrames = 120;

    // How often the local hand tools are re-resolved when one is missing. The lookup walks the user
    // hierarchy and allocates, so it must never be a per-frame cost.
    private const double ToolScanInterval = 1.0;

    private readonly World _world;
    private readonly List<DynamicBoneChain> _chains = new();
    private readonly List<DynamicBonePlayerColliders> _colliderSources = new();
    private readonly List<Tool> _localTools = new();
    private readonly List<bool> _localToolGrip = new();
    private UserRoot? _scannedRoot;
    private int _emptyToolScans;

    private static readonly Comparison<DynamicBoneChain> ByUpdateOrder =
        static (a, b) => a.UpdateOrder.CompareTo(b.UpdateOrder);

    private DynamicBoneColliderShape[] _playerColliders = new DynamicBoneColliderShape[16];
    private int _playerColliderCount;

    private bool _orderDirty;
    private ulong _frame = ulong.MaxValue;
    private int _cursor;
    private double _frameMilliseconds;
    private ulong _framesRun;
    private int _budgetStrikes;
    private bool _budgetReported;

    private float3 _localHead;
    private bool _hasLocalHead;
    private Slot? _localHeadSlot;
    private double _nextHeadScan = double.NegativeInfinity;

    private double _nextToolScan = double.NegativeInfinity;

    private DynamicBoneManager(World world)
    {
        _world = world;
    }

    // Created on first use. Null world yields null.
    public static DynamicBoneManager? For(World? world)
        => world == null ? null : Managers.GetValue(world, static w => new DynamicBoneManager(w));

    public World World => _world;

    public int ChainCount => _chains.Count;

    public int ColliderSourceCount => _colliderSources.Count;

    public int PlayerColliderCount => _playerColliderCount;

    // Wall time the last completed frame's chains cost, all passes summed.
    public double LastFrameMilliseconds => _frameMilliseconds;

    // Chains that actually ran a simulation step last frame (paused ones do not count).
    public int LastSimulatedChains { get; private set; }

    public bool BudgetExceeded => _budgetReported;

    internal DynamicBoneColliderShape[] PlayerColliders => _playerColliders;

    internal bool HasLocalHead => _hasLocalHead;

    internal float3 LocalHeadPosition => _localHead;

    internal void Register(DynamicBoneChain chain)
    {
        if (chain == null || _chains.Contains(chain))
            return;
        _chains.Add(chain);
        _orderDirty = true;
    }

    internal void Unregister(DynamicBoneChain chain)
    {
        if (chain == null)
            return;
        int index = _chains.IndexOf(chain);
        if (index < 0)
            return;
        _chains.RemoveAt(index);
        // The flush cursor indexes this list, so a removal mid-frame has to move it or the chain that
        // slid into the freed slot is skipped for that frame.
        if (index < _cursor)
            _cursor--;
    }

    internal void NoteUpdateOrderChanged() => _orderDirty = true;

    internal void Register(DynamicBonePlayerColliders source)
    {
        if (source != null && !_colliderSources.Contains(source))
            _colliderSources.Add(source);
    }

    internal void Unregister(DynamicBonePlayerColliders source)
    {
        if (source != null)
            _colliderSources.Remove(source);
    }

    // Called by every chain from its own input pass. The first call of a frame opens the frame; each
    // call then drains every chain at or before the caller's update order.
    internal void Tick(DynamicBoneChain caller)
    {
        if (_world == null || _world.IsDisposed || _world.IsDestroyed)
            return;

        // A backgrounded world's chains are not on screen and its render nodes are frozen anyway.
        if (_world.Focus == World.WorldFocus.Background)
            return;

        ulong frame = _world.Time?.UpdateIndex ?? 0;
        if (frame != _frame)
            BeginFrame(frame);

        if (_orderDirty)
        {
            _chains.Sort(ByUpdateOrder);
            _orderDirty = false;
            // The sort moved chains under the cursor; anything already simulated this frame is
            // protected by its own frame stamp, so restarting the cursor cannot double-step it.
            _cursor = 0;
        }

        int callerOrder = caller.UpdateOrder;
        // Clamped, not the raw average: SmoothDelta has no ceiling, so a load hitch hands the chains a
        // fifth-of-a-second step and hair whips instead of settling. Same reason the soft bodies moved
        // off it. -xlinka
        float delta = _world.Time?.Delta ?? (1f / 60f);
        long start = Stopwatch.GetTimestamp();

        while (_cursor < _chains.Count)
        {
            var chain = _chains[_cursor];
            if (chain == null || chain.IsDestroyed)
            {
                _chains.RemoveAt(_cursor);
                continue;
            }
            if (chain.UpdateOrder > callerOrder)
                break;

            _cursor++;
            if (RunChain(chain, delta))
                LastSimulatedChains++;
        }

        _frameMilliseconds += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        ReportBudget();
    }

    // Run this frame's shared gather if nothing has yet. Soft bodies read PlayerColliders too, and the
    // only thing that ever called BeginFrame was a chain ticking - so in a world with no dynamic bones
    // the set stayed empty for ever and cloth went back to not noticing anybody. Same guards as Tick.
    // -xlinka
    internal void EnsurePlayerColliders()
    {
        if (_world == null || _world.IsDisposed || _world.IsDestroyed)
            return;
        if (_world.Focus == World.WorldFocus.Background)
            return;

        ulong frame = _world.Time?.UpdateIndex ?? 0;
        if (frame != _frame)
            BeginFrame(frame);
    }

    private void BeginFrame(ulong frame)
    {
        _frame = frame;
        _cursor = 0;
        _frameMilliseconds = 0.0;
        LastSimulatedChains = 0;
        _framesRun++;

        ResolveLocalHead();
        GatherPlayerColliders();
        PollLocalGrabs();
    }

    private bool RunChain(DynamicBoneChain chain, float delta)
    {
        try
        {
            return chain.RunManagedSimulation(this, delta);
        }
        catch (Exception ex)
        {
            // One bad chain must not take the rest of the world's hair with it. Disable it so the
            // exception is reported once rather than every frame.
            Logger.Error($"DynamicBoneManager: chain simulation threw, disabling it - {ex.Message}");
            chain.Enabled.Value = false;
            return false;
        }
    }

    private void ReportBudget()
    {
        if (_budgetReported || _framesRun < BudgetWarmupFrames)
            return;
        if (_frameMilliseconds <= BudgetMilliseconds)
        {
            _budgetStrikes = 0;
            return;
        }
        if (++_budgetStrikes < BudgetStrikes)
            return;

        _budgetReported = true;
        Logger.Warn($"DynamicBoneManager: {_chains.Count} chains cost {_frameMilliseconds:F2}ms in a frame " +
                    $"(budget {BudgetMilliseconds:F2}ms, {BudgetStrikes} frames running, {LastSimulatedChains} simulated, " +
                    $"{_playerColliderCount} player colliders). Nothing was dropped; reported once per world.");
    }

    // The head slot is cached rather than asked for each frame: the body-node lookup behind it builds
    // a predicate to search the user's component registry, and that predicate is garbage this pass
    // must not be making sixty times a second. Re-resolved when the cached slot dies (respawn, avatar
    // swap), rate limited so a user with no head yet does not retry every frame. -xlinka
    private void ResolveLocalHead()
    {
        if (_localHeadSlot == null || _localHeadSlot.IsDestroyed)
        {
            _hasLocalHead = false;
            _localHeadSlot = null;

            double now = _world.Time?.TotalTime ?? 0d;
            if (now < _nextHeadScan)
                return;
            _nextHeadScan = now + ToolScanInterval;

            var head = _world.LocalUser?.Root?.HeadSlot;
            if (head == null || head.IsDestroyed)
                return;
            _localHeadSlot = head;
        }

        _localHead = _localHeadSlot.GlobalPosition;
        _hasLocalHead = true;
    }

    // PLAYER COLLIDERS
    //
    // Every peer builds this from the tracking it already has replicated, so touching someone's tail
    // costs no extra sync at all: your hand's position is on the wire because your hand is on the wire.
    // Two peers can disagree about the exact push by a frame of jitter, which is fine - the bone
    // simulation was never deterministic across peers to begin with.
    private void GatherPlayerColliders()
    {
        _playerColliderCount = 0;

        for (int i = _colliderSources.Count - 1; i >= 0; i--)
        {
            var source = _colliderSources[i];
            if (source == null || source.IsDestroyed)
            {
                _colliderSources.RemoveAt(i);
                continue;
            }
            source.Contribute(this);
        }
    }

    // Called by the collider sources during the gather. Grows the backing array; steady state never
    // reallocates because the user count settles. The shape carries its own world AABB so a chain can
    // rule a hand out across the room without touching it.
    internal void AddPlayerCollider(in float3 a, in float3 b, float radius, User? owner)
    {
        if (radius <= 0f)
            return;
        if (_playerColliderCount >= _playerColliders.Length)
            Array.Resize(ref _playerColliders, _playerColliders.Length * 2);

        _playerColliders[_playerColliderCount++] = float3.DistanceSquared(a, b) > 1e-8f
            ? DynamicBoneColliderShape.FromCapsule(in a, in b, radius, owner)
            : DynamicBoneColliderShape.FromSphere(in a, radius, owner);
    }

    // LOCAL HAND GRAB
    //
    // Bones carry no colliders, so the hand's physical-overlap grab can never find one. The poll runs
    // the other way round: the manager knows where the hands are (it just gathered them as colliders)
    // and asks the grabbable chains whether a bone is inside reach on the frame the grip closes.
    //
    // The capture goes through Grabber.TryGrab, NOT straight into the chain, so the hand records the
    // hold like any other: grip release runs ReleaseAll and the chain lets go with everything else,
    // and holder arbitration is the same code path a prop uses. -xlinka
    private void PollLocalGrabs()
    {
        var root = _world.LocalUser?.Root;
        if (root == null || root.IsDestroyed)
        {
            _localTools.Clear();
            _localToolGrip.Clear();
            return;
        }

        bool anyGrabbable = false;
        for (int i = 0; i < _chains.Count; i++)
        {
            var chain = _chains[i];
            if (chain != null && !chain.IsDestroyed && chain.Enabled.Value && chain.AllowGrab.Value)
            {
                anyGrabbable = true;
                break;
            }
        }
        if (!anyGrabbable)
            return;

        RefreshLocalTools(root);

        for (int i = 0; i < _localTools.Count; i++)
        {
            var tool = _localTools[i];
            if (tool == null || tool.IsDestroyed)
                continue;

            bool grip = tool.GripHeld;
            bool wasHeld = _localToolGrip[i];
            _localToolGrip[i] = grip;
            if (!grip || wasHeld)
                continue;

            var grabber = tool.Grabber;
            // A hand that already closed on a prop this frame is busy. The tool resolves its own grab
            // before this poll ever sees the edge, so this is the honest answer, not a race.
            if (grabber == null || grabber.IsDestroyed || grabber.IsHoldingObjects)
                continue;

            TryGrabNearestChain(grabber);
        }
    }

    private void TryGrabNearestChain(Grabber grabber)
    {
        var hand = grabber.Slot;
        if (hand == null || hand.IsRemoved)
            return;

        float3 point = hand.GlobalPosition;
        DynamicBoneChain? best = null;
        int bestBone = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < _chains.Count; i++)
        {
            var chain = _chains[i];
            if (chain == null || chain.IsDestroyed || !chain.Enabled.Value)
                continue;
            if (!chain.CanGrab(grabber))
                continue;

            int bone = chain.FindGrabbableBone(point, out float distance);
            if (bone < 0 || distance >= bestDistance)
                continue;

            best = chain;
            bestBone = bone;
            bestDistance = distance;
        }

        best?.BeginHandGrab(grabber, bestBone);
    }

    private void RefreshLocalTools(UserRoot root)
    {
        if (!ReferenceEquals(root, _scannedRoot))
        {
            _scannedRoot = root;
            _emptyToolScans = 0;
            _localTools.Clear();
            _localToolGrip.Clear();
        }

        bool stale = _localTools.Count == 0;
        for (int i = 0; i < _localTools.Count && !stale; i++)
        {
            var tool = _localTools[i];
            if (tool == null || tool.IsDestroyed || tool.Slot == null || tool.Slot.IsRemoved)
                stale = true;
        }
        if (!stale)
            return;

        // A user whose hands came up empty a few scans running has no hand tools, full stop - a
        // headless host, a spectator, a body still being built by someone else. Give up until the
        // root itself is replaced rather than walking that hierarchy once a second forever. -xlinka
        if (_localTools.Count == 0 && _emptyToolScans >= 3)
            return;

        double now = _world.Time?.TotalTime ?? 0d;
        if (now < _nextToolScan)
            return;
        _nextToolScan = now + ToolScanInterval;

        _localTools.Clear();
        _localToolGrip.Clear();
        var slot = root.Slot;
        if (slot == null || slot.IsRemoved)
            return;

        foreach (var tool in slot.GetComponentsInChildren<HandTool>())
        {
            _localTools.Add(tool);
            _localToolGrip.Add(tool.GripHeld);
        }
        _emptyToolScans = _localTools.Count == 0 ? _emptyToolScans + 1 : 0;
    }
}
