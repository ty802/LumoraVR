// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

public sealed class GradientStop<T> : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Position = new();

    public readonly Sync<T> Value = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Position", Position.Save(control));
        dictionary.Add("Value", Value.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Position") is { } positionNode)
            Position.Load(positionNode, control);
        if (dictionary.TryGetNode("Value") is { } valueNode)
            Value.Load(valueNode, control);
    }

    public override object? GetValueAsObject() => Value.Value;
}

// Drives a field from a keyed gradient sampled by a 0..1 progress value.
//
// Sorting happens at sample time rather than by keeping the list ordered. The stop list is a
// replicated collection anyone can edit, so an authored order would have to be re-imposed on every
// peer after every insert, and a stop dragged past its neighbour would reorder the datamodel under
// whoever else was editing it. Reading in sorted order costs a scan of a handful of stops. -xlinka
//
// LumoraMath's ColorGradient and FloatCurve are deliberately not used here: their keys are plain
// arrays, not sync members, so a gradient built on them could not be edited in the inspector or
// replicated stop by stop.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(
    typeof(float), typeof(double), typeof(int), typeof(long),
    typeof(float2), typeof(float3), typeof(float4), typeof(floatQ),
    typeof(color), typeof(colorHDR))]
public class ValueGradient<T> : Component
{
    // Point a drive or a mapper at this.
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Progress;

    // Off snaps to the stop at or before the progress.
    public readonly Sync<bool> Interpolate;

    public readonly SyncList<GradientStop<T>> Stops;

    public readonly FieldDrive<T> Target;

    public static bool IsValidGenericType => DrivenValueTypes.SupportsBlend(typeof(T));

    public ValueGradient()
    {
        Progress = new Sync<float>(this, 0f);
        Interpolate = new Sync<bool>(this, true);
        Stops = new SyncList<GradientStop<T>>();
        Target = new FieldDrive<T>(this) { LocalValueOnly = true };
    }

    public GradientStop<T> AddStop(float position, T value)
    {
        var stop = Stops.Add();
        stop.Position.Value = position;
        stop.Value.Value = value;
        return stop;
    }

    // Returns the type default when there are no stops.
    public T Sample(float progress)
    {
        GradientStop<T>? before = null;
        GradientStop<T>? after = null;

        foreach (var stop in Stops.Elements)
        {
            float position = stop.Position.Value;
            if (position <= progress)
            {
                if (before == null || position > before.Position.Value)
                    before = stop;
            }
            else if (after == null || position < after.Position.Value)
            {
                after = stop;
            }
        }

        if (before == null && after == null)
            return SyncCoder.GetDefault<T>();
        if (before == null)
            return after!.Value.Value;
        if (after == null || !Interpolate.Value)
            return before.Value.Value;

        var blend = ValueOps<T>.Blend;
        if (blend == null)
            return before.Value.Value;

        float span = after.Position.Value - before.Position.Value;
        // Two stops keyed at the same position have no span to interpolate across; take the earlier one
        // rather than dividing by zero.
        if (span <= 0f)
            return before.Value.Value;
        return blend(before.Value.Value, after.Value.Value, (progress - before.Position.Value) / span);
    }

    public override void OnUpdate(float delta)
    {
        if (Stops.Count > 0)
            Target.SetValue(Sample(Progress.Value));
    }
}
