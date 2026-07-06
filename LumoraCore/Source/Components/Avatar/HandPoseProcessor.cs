// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Base for a finger-pose source that derives its pose from upstream sources
/// instead of from hardware. Subclasses (lerp, modifier, ...) implement
/// <see cref="Evaluate"/>; this base handles the once-per-frame caching, the
/// <see cref="IHandPoseSource"/> read surface, and exception isolation.
/// </summary>
// Derived poses are recomputed at most once per frame: the first read in a frame
// runs Evaluate and fills a per-node cache, later reads in the same frame hit the
// cache. If Evaluate throws, the component disables itself (one warning) rather
// than spamming an exception every frame from a broken graph. Like the rest of the
// finger pipeline this is local-only - no derived data goes on the wire. -xlinka
public abstract class HandPoseProcessor : UserRootComponent, IHandPoseSourceComponent
{
    /// <summary>The two hands a derived pose covers.</summary>
    protected static readonly Chirality[] Sides = { Chirality.Left, Chirality.Right };

    // Wrist-local position per finger node for the frame currently cached.
    private readonly Dictionary<BodyNode, float3> _cache = new();
    private bool _leftTracked;
    private bool _rightTracked;
    private bool _tracksMetacarpals;

    private long _cachedFrame = -1;
    private bool _faulted;

    /// <summary>The derived pose carries metacarpals only if its evaluation produces them.</summary>
    public bool TracksMetacarpals
    {
        get { EnsureEvaluated(); return _tracksMetacarpals; }
    }

    public bool IsHandTracked(Chirality side)
    {
        EnsureEvaluated();
        return side == Chirality.Left ? _leftTracked
             : side == Chirality.Right ? _rightTracked
             : false;
    }

    public bool TryGetFingerPosition(BodyNode node, out float3 wristLocalPosition)
    {
        EnsureEvaluated();
        return _cache.TryGetValue(node, out wristLocalPosition);
    }

    /// <summary>
    /// Recompute the derived pose for this frame. Subclasses read their upstream
    /// sources and publish results through <see cref="Set"/> / <see cref="SetTracking"/>
    /// / <see cref="SetTracksMetacarpals"/>. Called at most once per frame; the cache
    /// is already cleared and tracking flags reset to false on entry.
    /// </summary>
    protected abstract void Evaluate();

    /// <summary>Publish one node's wrist-local position into this frame's pose.</summary>
    protected void Set(BodyNode node, float3 wristLocalPosition)
        => _cache[node] = wristLocalPosition;

    /// <summary>Mark a side as carrying valid derived data this frame.</summary>
    protected void SetTracking(Chirality side, bool tracking)
    {
        if (side == Chirality.Left) _leftTracked = tracking;
        else if (side == Chirality.Right) _rightTracked = tracking;
    }

    /// <summary>Declare whether the derived pose includes metacarpal nodes.</summary>
    protected void SetTracksMetacarpals(bool value) => _tracksMetacarpals = value;

    /// <summary>
    /// Per-side node-count check a subclass can use to validate an upstream source's
    /// coverage. Counts how many of the side's nodes the source actually provides.
    /// </summary>
    protected static int CountProvidedNodes(IHandPoseSource source, Chirality side)
    {
        if (source == null)
            return 0;
        int count = 0;
        foreach (var node in HandPoseNodes.NodesOf(side))
            if (source.TryGetFingerPosition(node, out _))
                count++;
        return count;
    }

    // Run Evaluate once per frame, guarded. On the first throw the component
    // disables itself so a broken graph stops costing anything; the cache from the
    // last good frame (if any) is cleared so we don't serve a half-built pose.
    private void EnsureEvaluated()
    {
        if (_faulted)
            return;

        long frame = Engine.Current?.FrameCount ?? 0;
        if (frame == _cachedFrame)
            return;
        _cachedFrame = frame;

        _cache.Clear();
        _leftTracked = false;
        _rightTracked = false;
        _tracksMetacarpals = false;

        try
        {
            Evaluate();
        }
        catch (Exception ex)
        {
            _faulted = true;
            _cache.Clear();
            _leftTracked = false;
            _rightTracked = false;
            LumoraLogger.Warn($"{GetType().Name}: finger-pose evaluation threw, disabling. {ex.Message}");
            Enabled.Value = false;
        }
    }
}
