// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// A shape a released item is pulled onto: a point, a grid, a rail, a surface. Unlike a socket a
// guide does not take ownership of the item, it only corrects where the item came to rest.
//
// Sockets and guides answer two different questions. A socket asks "does this belong to me", so it
// reparents and books the item as its occupant. A guide asks "where along me is the nearest legal
// spot", so it moves the item and walks away. Keeping them apart means a shelf of sockets and a
// build grid can cover the same volume without arguing over the same object. Sockets win when both
// apply: a receptacle the builder placed on purpose beats an ambient grid. -xlinka
public abstract class MagnetGuide : Component
{
    // Also take the guide's orientation, not just its position.
    public readonly Sync<bool> AlignRotation;

    // How far from the guide a released item still gets captured, in the guide's local scale.
    public readonly Sync<float> Range;

    // Seconds the item takes to glide onto the constrained pose. 0 places it instantly.
    public readonly Sync<float> SettleTime;

    // Empty leaves parentage alone.
    public readonly SyncRef<Slot> AttachUnder;

    private MagnetRegistry? _registry;

    protected MagnetGuide()
    {
        AlignRotation = new Sync<bool>(this, true);
        Range = new Sync<float>(this, 0.5f);
        SettleTime = new Sync<float>(this, 0.1f);
        AttachUnder = new SyncRef<Slot>(this);
    }

    // Range in world units.
    public float WorldRange => Slot != null ? Slot.LocalScaleToGlobal(System.Math.Max(0f, Range.Value)) : 0f;

    // True when this guide is live and worth asking.
    public bool IsUsable => !IsDestroyed && Enabled.Value && Slot != null && !Slot.IsDestroyed && Slot.IsActive;

    // False when the shape is degenerate (a zero-length rail, a point dead on a ring's axis) and has
    // no answer to give.
    public abstract bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation);

    public override void OnStart()
    {
        base.OnStart();
        _registry = MagnetRegistry.For(World);
        _registry?.Register(this);
    }

    public override void OnDestroy()
    {
        _registry?.Unregister(this);
        _registry = null;
        base.OnDestroy();
    }
}
