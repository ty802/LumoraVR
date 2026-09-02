// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Reversible slot pose change: parent plus local position/rotation/scale, captured before the
// edit (Begin) and after it (Commit). Covers transform resets, reparenting and bring-to moves.
//
// A pose whose recorded parent has since been destroyed is refused rather than half-applied. The old
// behaviour skipped the reparent and wrote the local values anyway, which teleported the slot to a
// pose that means nothing under its current parent and still reported success. -xlinka
public sealed class SlotTransformUndoBatch : IUndoBatch, IUndoTargetQuery
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

    public LocaleText LocalizedDescription { get; }

    public string Description => LocalizedDescription.Resolve();

    private SlotTransformUndoBatch(Slot slot, LocaleText description)
    {
        _slot = slot;
        LocalizedDescription = description;
        _before = Capture(slot);
    }

    public static SlotTransformUndoBatch? Begin(Slot? slot, LocaleText description)
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

    public bool ReferencesElement(IWorldElement element)
    {
        if (element is not Slot slot)
            return false;
        return UndoTargets.Touches(_slot, slot)
            || UndoTargets.Touches(_before.Parent, slot)
            || UndoTargets.Touches(_after.Parent, slot);
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
        if (!UndoTargets.CanRestoreUnder(pose.Parent))
            return false;

        // Local values are meaningless under the wrong parent, so restore parentage first.
        if (pose.Parent != null && !ReferenceEquals(_slot.Parent, pose.Parent))
            _slot.SetParent(pose.Parent);

        _slot.LocalPosition.Value = pose.Position;
        _slot.LocalRotation.Value = pose.Rotation;
        _slot.LocalScale.Value = pose.Scale;
        return true;
    }
}
