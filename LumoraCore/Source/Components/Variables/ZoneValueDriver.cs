// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.Variables;

// Drives a field with the value of the named zones this slot stands in, or DefaultValue outside them all.
//
// Worked out on every machine and written locally: a zone that follows the local user (a light culling
// sphere around the viewer, say) gives every viewer a different answer, and none of them is anyone
// else's business. One pass over the zones under one name per frame, nothing allocated. -xlinka
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ZoneValueDriver<T> : Component
{
    public readonly FieldDrive<T> Target;

    public readonly Sync<string> ZoneName;

    public readonly Sync<T> DefaultValue;

    public readonly Sync<ZoneValueMode> Mode;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    private struct Hit
    {
        public T Value;
        public float Weight;
        public int Priority;
        public RefID Id;
    }

    // Lowest priority first, then the shallower, so a blend lays the strongest zone down last.
    private static readonly Comparison<Hit> Ascending = static (a, b) =>
    {
        int c = a.Priority.CompareTo(b.Priority);
        if (c != 0)
            return c;
        c = a.Weight.CompareTo(b.Weight);
        return c != 0 ? c : b.Id.CompareTo(a.Id);
    };

    private string? _name;
    private List<IValueZone>? _zones;
    private readonly List<Hit> _hits = new();

    public ZoneValueDriver()
    {
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
        ZoneName = new Sync<string>(this, string.Empty);
        DefaultValue = new Sync<T>(this, Networking.Sync.SyncCoder.GetDefault<T>());
        Mode = new Sync<ZoneValueMode>(this, ZoneValueMode.HighestPriority);
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid || Slot is not { } slot || World is not { } world)
            return;

        string? name = ValueZoneRegistry.Filter(ZoneName.Value);
        if (name != _name || (_zones == null && name != null))
        {
            _name = name;
            _zones = name == null ? null : ValueZoneRegistry.ListFor(world, name);
        }
        Target.SetValue(Sample(slot.GlobalPosition));
    }

    private T Sample(in float3 point)
    {
        var zones = _zones;
        T fallback = DefaultValue.Value;
        if (zones == null || zones.Count == 0)
            return fallback;

        var mode = Mode.Value;
        if (mode != ZoneValueMode.HighestPriority && ValueOps<T>.Blend == null)
            mode = ZoneValueMode.HighestPriority;

        bool found = false;
        Hit best = default;
        _hits.Clear();
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i] is not IValueZone<T> zone || !zone.TrySample(in point, out var value, out float weight))
                continue;
            var hit = new Hit { Value = value, Weight = weight, Priority = zone.Priority, Id = zone.ReferenceID };
            if (mode == ZoneValueMode.HighestPriority)
            {
                if (!found || Ascending(hit, best) > 0)
                    best = hit;
                found = true;
            }
            else
            {
                _hits.Add(hit);
            }
        }

        if (mode == ZoneValueMode.HighestPriority)
            return found ? best.Value : fallback;
        if (_hits.Count == 0)
            return fallback;

        var blend = ValueOps<T>.Blend!;
        T result = fallback;
        switch (mode)
        {
            case ZoneValueMode.WeightedAverage:
            {
                float total = 0f;
                T mean = default!;
                foreach (var hit in _hits)
                {
                    if (!(hit.Weight > 0f))
                        continue;
                    total += hit.Weight;
                    mean = total == hit.Weight ? hit.Value : blend(mean, hit.Value, hit.Weight / total);
                }
                if (total > 0f)
                    result = total < 1f ? blend(fallback, mean, total) : mean;
                break;
            }
            case ZoneValueMode.PrioritySortedBlend:
                _hits.Sort(Ascending);
                foreach (var hit in _hits)
                    result = blend(result, hit.Value, hit.Weight);
                break;
            case ZoneValueMode.Additive:
            {
                var step = ValueOps<T>.Step;
                if (step == null)
                    break;
                foreach (var hit in _hits)
                    result = step(result, blend(default!, hit.Value, hit.Weight));
                break;
            }
        }
        _hits.Clear();
        return result;
    }
}
