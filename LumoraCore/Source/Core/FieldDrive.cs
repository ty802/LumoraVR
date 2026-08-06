// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

// A link that PRODUCES a value field's value. Declared as a readonly member on the driving worker:
//
//     public readonly FieldDrive<bool> CheckVisual = new();
//
// The target is a normal replicated, persisted reference, so a drive authored on one machine exists on
// every peer and comes back after a save. Each peer grants its own copy of the link against its own copy
// of the target (see LinkManager), and a driven field is excluded from value sync in both directions
// (see SyncElement.InvalidateSyncElement / ConflictingSyncElement.Validate) because the value is DERIVED
// - every peer computes it from the driver's own replicated inputs. -xlinka
public class FieldDrive<T> : FieldHook<T>
{
    private Func<T>? _valueSource;

    public FieldDrive()
    {
    }

    public FieldDrive(IWorldElement? owner) : base(owner)
    {
    }

    public override bool IsDriving => true;

    // A driven field still takes hand writes. Deliberate: reparenting writes LocalPosition on driven
    // avatar slots, and refusing that would break equip. The drive simply wins again on its next pass.
    public override bool IsModificationAllowed => true;

    // When true, driven writes update the field locally without generating
    // sync data. Use when the drive's inputs already replicate and the drive
    // runs on every peer - each peer computes the same value itself, so
    // broadcasting the result would double the traffic and fight the remote
    // computation. Leave false for drives that exist on a single peer.
    public bool LocalValueOnly { get; set; }

    public IField<T>? Field => Target;

    // The value currently sitting in the driven field. Named apart from Value,
    // which on a link member is the target's RefID, not the driven value.
    public T DrivenValue
    {
        get => Target is { } field ? field.Value : default!;
        set => SetValue(value);
    }

    public void DriveFrom(Func<T> source)
    {
        _valueSource = source;
    }

    // Passing null releases the current target.
    public void DriveTarget(IField<T>? target)
    {
        Target = target!;
    }

    public void UpdateDrive()
    {
        if (!IsLinkValid || _valueSource == null)
        {
            return;
        }

        try
        {
            SetValue(_valueSource());
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"FieldDrive UpdateDrive error: {ex.Message}");
        }
    }

    // Silently does nothing unless the link was granted.
    public void SetValue(T value)
    {
        if (!IsLinkValid || Target is not SyncField<T> syncTarget)
        {
            return;
        }

        if (LocalValueOnly)
        {
            syncTarget.SetDrivenValueLocal(value);
        }
        else
        {
            syncTarget.SetDrivenValue(value);
        }
    }

    // Build a drive that is NOT a member of anything: a local-allocation element, so it never replicates
    // and never saves. For drives whose count and targets are discovered per peer at runtime and would be
    // nonsense to replicate - a rig's finger bones, for instance. Dispose it when done. -xlinka
    public static FieldDrive<T> CreateLocal(IWorldElement owner)
    {
        var world = owner?.World ?? throw new ArgumentNullException(nameof(owner));
        var controller = world.ReferenceController
            ?? throw new InvalidOperationException("World has no reference controller");

        controller.LocalAllocationBlockBegin();
        try
        {
            var drive = new FieldDrive<T>();
            drive.MarkNonPersistent();
            drive.Initialize(world, owner);
            drive.EndInitPhase();
            return drive;
        }
        finally
        {
            controller.LocalAllocationBlockEnd();
        }
    }
}

public static class FieldDriveExtensions
{
    // Detached convenience drive over a field, pulled from a source function. Local-only; a drive
    // meant to replicate belongs on a worker as a declared member.
    public static FieldDrive<T> CreateDrive<T>(this SyncField<T> target, Func<T> source)
    {
        var drive = FieldDrive<T>.CreateLocal(target);
        drive.DriveFrom(source);
        drive.DriveTarget(target);
        return drive;
    }
}
