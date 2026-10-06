// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Variables;

// A named value that holds inside a region of space. Drivers ask by name and by point (ZoneValueDriver),
// never by reference, so a room can carry its own value without anything pointing at it.
//
// Registered under its name for as long as it exists; switched off or on an inactive slot it is still in
// the list but answers nothing, which keeps the list stable while a toggle flicks it. -xlinka
public abstract class ValueZone<T> : Component, IValueZone<T>
{
    public readonly Sync<string> ZoneName;

    // Higher wins when zones overlap.
    public readonly Sync<int> Priority;

    private string? _registered;

    protected ValueZone()
    {
        ZoneName = new Sync<string>(this, string.Empty);
        Priority = new Sync<int>(this, 0);
    }

    string? IValueZone.ZoneName => _registered;

    int IValueZone.Priority => Priority.Value;

    public override void OnStart()
    {
        base.OnStart();
        Register();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Register();
    }

    public override void OnDestroy()
    {
        Unregister();
        base.OnDestroy();
    }

    private void Register()
    {
        var world = World;
        if (world == null || IsDestroyed)
            return;
        string? name = ValueZoneRegistry.Filter(ZoneName.Value);
        if (name == _registered)
            return;
        Unregister();
        if (name == null)
            return;
        _registered = name;
        ValueZoneRegistry.Add(world, name, this);
    }

    private void Unregister()
    {
        if (_registered != null && World is { } world)
            ValueZoneRegistry.Remove(world, _registered, this);
        _registered = null;
    }

    public bool TrySample(in float3 globalPoint, out T value, out float weight)
    {
        var slot = Slot;
        if (slot == null || IsDestroyed || !Enabled.Value || !slot.IsActive)
        {
            value = default!;
            weight = 0f;
            return false;
        }
        return SampleLocal(slot.GlobalPointToLocal(in globalPoint), out value, out weight);
    }

    protected abstract bool SampleLocal(in float3 localPoint, out T value, out float weight);

    // Depth over blend distance, clamped. No blend distance means full weight the moment the point is in.
    protected static float Weight(float depth, float blend)
    {
        if (!(blend > 0f))
            return 1f;
        float w = depth / blend;
        return w <= 0f ? 0f : w >= 1f ? 1f : w;
    }
}
