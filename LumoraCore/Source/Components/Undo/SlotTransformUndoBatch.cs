// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Reversible slot pose change: parent plus local position/rotation/scale, captured before the
// edit (Begin) and after it (Commit). Covers transform resets, reparenting and bring-to moves.
public sealed class SlotTransformUndoBatch : IUndoBatch
{
    private struct Pose
    {
        public Slot? Parent;
        public float3 Position;
        public floatQ Rotation;
        public float3 Scale;
    }

    private readonly Slot _slot;
    private readonly Pose _before;
    private Pose _after;

    public string Description { get; }

    private SlotTransformUndoBatch(Slot slot, string description)
    {
        _slot = slot;
        Description = description;
        _before = Capture(slot);
    }

    public static SlotTransformUndoBatch? Begin(Slot? slot, string description)
    {
        if (slot == null || slot.IsDestroyed)
            return null;
        return new SlotTransformUndoBatch(slot, description);
    }

    // null when nothing actually changed, so a no-op action never lands in the history
    public SlotTransformUndoBatch? Commit()
    {
        if (_slot.IsDestroyed)
            return null;
        _after = Capture(_slot);
        return PoseEquals(_before, _after) ? null : this;
    }

    public bool Undo() => Apply(_before);

    public bool Redo() => Apply(_after);

    public void OnEvicted()
    {
    }

    private static Pose Capture(Slot slot) => new Pose
    {
        Parent = slot.Parent,
        Position = slot.LocalPosition.Value,
        Rotation = slot.LocalRotation.Value,
        Scale = slot.LocalScale.Value,
    };

    private static bool PoseEquals(in Pose a, in Pose b)
        => ReferenceEquals(a.Parent, b.Parent)
        && a.Position.Equals(b.Position)
        && a.Rotation.Equals(b.Rotation)
        && a.Scale.Equals(b.Scale);

    private bool Apply(in Pose pose)
    {
        if (_slot == null || _slot.IsDestroyed)
            return false;

        // Local values are meaningless under the wrong parent, so restore parentage first.
        if (pose.Parent != null && !pose.Parent.IsDestroyed && !ReferenceEquals(_slot.Parent, pose.Parent))
            _slot.SetParent(pose.Parent);

        _slot.LocalPosition.Value = pose.Position;
        _slot.LocalRotation.Value = pose.Rotation;
        _slot.LocalScale.Value = pose.Scale;
        return true;
    }
}
