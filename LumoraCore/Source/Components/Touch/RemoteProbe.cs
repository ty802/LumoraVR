// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// A pointer beam standing in for a fingertip. Its "tip" is wherever the laser is landing, and it
// counts as pressing while the laser's primary is held.
//
// Keeps touch controls from being a VR-only feature: a desktop user has no fingertip to
// put through a button, so every touch control that accepts remote touch is reachable with the same
// laser they use for everything else, with real hold semantics rather than a one-shot click.
//
// It deliberately reads the laser's OWN resolved hit rather than casting again. The laser already
// decides what the pointer may touch this frame - the dash swallows the pointer while it is open,
// a carried object suppresses presses, a dormant laser touches nothing - and re-casting here would
// hand the user a second pointer that ignores every one of those rules. -xlinka
//
// Runs after the hand tool (order 0), so a beam probe reads the pose and press state the tool pushed
// into the laser THIS frame instead of last frame's. -xlinka
[DefaultUpdateOrder(100)]
[ComponentCategory("Interaction/Touch")]
public class RemoteProbe : TouchProbe
{
    // Found on this slot or above it when left empty.
    public readonly SyncRef<InteractionLaser> Laser;

    private InteractionLaser? _laserCache;
    private double _nextLaserSearch;
    private readonly List<InteractionLaser> _laserBuffer = new(2);

    // The hand tool builds its beam lazily, so a probe that starts first has to keep looking.
    private const double LaserSearchInterval = 0.25;

    public RemoteProbe()
    {
        Laser = new SyncRef<InteractionLaser>(this);
    }

    public override TouchProbeKind Kind => TouchProbeKind.Remote;

    public override float3 TipPosition => ResolveLaser()?.CurrentHitPoint ?? float3.Zero;

    public override float3 TipDirection => ResolveLaser()?.RayDirection ?? float3.Forward;

    // Resolved through the explicit ref or the hierarchy.
    public InteractionLaser? ActiveLaser => ResolveLaser();

    public override void OnStart()
    {
        base.OnStart();
        BindOwnerFromHierarchy();
    }

    protected override bool Sample(out TouchSample sample)
    {
        sample = default;

        var laser = ResolveLaser();
        if (laser == null)
            return false;

        var hitSlot = laser.CurrentHitSlot;
        if (hitSlot == null || hitSlot.IsDestroyed || laser.CurrentTarget == null)
            return false;

        var target = ResolveTarget(hitSlot);
        if (target == null)
            return false;

        // A beam has no depth to give, so it reports its press as a bare on/off: exactly at the
        // surface while held, exactly one hover margin short while not. Depth-driven controls turn
        // that into a full stroke themselves rather than trying to read travel out of a ray. -xlinka
        float penetration = laser.ToolPrimaryPressed ? 0f : -MathF.Max(HoverMargin.Value, 0.001f);
        float3 point = laser.CurrentHitPoint;
        float3 normal = -laser.RayDirection;

        sample = new TouchSample(target, in point, in normal, penetration);
        return true;
    }

    // The beam lives under the hand tool's own rig, which is a SIBLING of this probe under the same
    // controller node, so neither a parent walk nor a self lookup finds it. Sweep the controller
    // subtree and take the beam on this hand's side. Throttled, because the hand tool builds its rig
    // lazily and this would otherwise sweep every frame for the first second of the session. -xlinka
    private InteractionLaser? ResolveLaser()
    {
        var explicitLaser = Laser.Target;
        if (explicitLaser != null && !explicitLaser.IsDestroyed)
            return explicitLaser;

        if (_laserCache != null && !_laserCache.IsDestroyed)
            return _laserCache;

        _laserCache = null;

        double now = World?.Time.TotalTime ?? 0.0;
        if (now < _nextLaserSearch)
            return null;
        _nextLaserSearch = now + LaserSearchInterval;

        var own = Slot?.GetComponent<InteractionLaser>();
        if (own != null && Matches(own))
        {
            _laserCache = own;
            return own;
        }

        var scope = Slot?.Parent ?? Slot;
        if (scope == null || scope.IsDestroyed)
            return null;

        _laserBuffer.Clear();
        scope.GetComponentsInChildren(_laserBuffer);
        for (int i = 0; i < _laserBuffer.Count; i++)
        {
            var candidate = _laserBuffer[i];
            if (candidate != null && Matches(candidate))
            {
                _laserCache = candidate;
                return candidate;
            }
        }
        return null;
    }

    private bool Matches(InteractionLaser laser)
    {
        if (laser.IsDestroyed)
            return false;
        if (Hand.Value == Chirality.None)
        {
            Hand.Value = laser.ControllerSide.Value;
            return true;
        }
        return laser.ControllerSide.Value == Hand.Value;
    }
}
