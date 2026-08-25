// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// One undo step covering a whole carry: where the item sat before it was picked up, where it
// ended up after the snap resolved, and which socket claimed it.
//
// SlotTransformUndoBatch captures the "before" pose the instant it is begun, and by the time a
// grabbable raises its grabbed event the item is ALREADY reparented under the hand with its world
// pose preserved. Capturing there would record "under the holder slot", and undo would shove the
// object back into a hand that has since moved. So this batch takes the before-pose as explicit
// values, which the magnet reconstructs from the grabbable's recorded pre-grab parent, and the
// whole drag plus the snap collapses into a single step. -xlinka
public sealed class MagnetPlacementUndo : IUndoBatch
{
    private struct Pose
    {
        public Slot? Parent;
        public float3 Position;
        public floatQ Rotation;
        public float3 Scale;
    }

    private readonly Slot _slot;
    private readonly Magnet _magnet;
    private readonly Pose _before;
    private readonly MagnetSocket? _socketBefore;
    private Pose _after;
    private MagnetSocket? _socketAfter;

    public string Description { get; }

    private MagnetPlacementUndo(Slot slot, Magnet magnet, in Pose before, MagnetSocket? socketBefore, string description)
    {
        _slot = slot;
        _magnet = magnet;
        _before = before;
        _socketBefore = socketBefore;
        Description = description;
    }

    // Null when there is nothing to track.
    public static MagnetPlacementUndo? Begin(
        Magnet magnet,
        Slot? parent,
        in float3 position,
        in floatQ rotation,
        in float3 scale,
        MagnetSocket? socketBefore,
        string description)
    {
        var slot = magnet?.Slot;
        if (magnet == null || slot == null || slot.IsDestroyed)
            return null;

        var pose = new Pose { Parent = parent, Position = position, Rotation = rotation, Scale = scale };
        return new MagnetPlacementUndo(slot, magnet, in pose, socketBefore, description);
    }

    // The end pose is passed in rather than read off the slot because the settle glide is still
    // running and the live values are mid-flight. Returns null when nothing actually moved, so an
    // aborted carry never lands in the history.
    public MagnetPlacementUndo? Commit(
        MagnetSocket? socketAfter,
        Slot? parent,
        in float3 position,
        in floatQ rotation,
        in float3 scale)
    {
        if (_slot.IsDestroyed)
            return null;

        _socketAfter = socketAfter;
        _after = new Pose { Parent = parent, Position = position, Rotation = rotation, Scale = scale };

        if (ReferenceEquals(_socketBefore, _socketAfter) && PoseEquals(in _before, in _after))
            return null;
        return this;
    }

    public bool Undo() => Apply(in _before, _socketAfter, _socketBefore);

    public bool Redo() => Apply(in _after, _socketBefore, _socketAfter);

    public void OnEvicted()
    {
    }

    private static bool PoseEquals(in Pose a, in Pose b)
        => ReferenceEquals(a.Parent, b.Parent)
        && a.Position.Equals(b.Position)
        && a.Rotation.Equals(b.Rotation)
        && a.Scale.Equals(b.Scale);

    private bool Apply(in Pose pose, MagnetSocket? vacate, MagnetSocket? claim)
    {
        if (_slot.IsDestroyed || _magnet.IsDestroyed)
            return false;

        // Free the socket the item is leaving before anything reparents, or the socket it moves to
        // would see two occupants for an instant and refuse the claim.
        if (vacate != null && !vacate.IsDestroyed)
            vacate.ReleaseItem(_magnet);

        // Local values mean nothing under the wrong parent, so parentage first.
        if (pose.Parent != null && !pose.Parent.IsDestroyed && !ReferenceEquals(_slot.Parent, pose.Parent))
            _slot.SetParent(pose.Parent);

        _slot.LocalPosition.Value = pose.Position;
        _slot.LocalRotation.Value = pose.Rotation;
        _slot.LocalScale.Value = pose.Scale;

        if (claim != null && !claim.IsDestroyed)
            claim.Claim(_magnet);
        return true;
    }
}
