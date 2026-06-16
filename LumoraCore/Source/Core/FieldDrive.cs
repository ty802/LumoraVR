// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

public class FieldDrive<T> : FieldHook<T>
{
    private Func<T>? _valueSource;

    public override bool IsDriving => true;

    public override bool IsModificationAllowed => true;

    /// <summary>
    /// When true, driven writes update the field locally without generating
    /// sync data. Use when the drive's inputs already replicate and the drive
    /// runs on every peer - each peer computes the same value itself, so
    /// broadcasting the result would double the traffic and fight the remote
    /// computation. Leave false for drives that exist on a single peer.
    /// </summary>
    public bool LocalValueOnly { get; set; }

    public IField<T>? Field => Target as IField<T>;

    public T Value
    {
        get => Field != null ? Field.Value : default!;
        set => SetValue(value);
    }

    public FieldDrive(World world) : base(world)
    {
    }

    public void DriveFrom(Func<T> source)
    {
        _valueSource = source;
    }

    public void DriveTarget(SyncField<T>? target)
    {
        if (target == null)
        {
            ReleaseLink();
            return;
        }

        HookTarget(target);
    }

    public void DriveTarget(IField<T>? target)
    {
        if (target == null)
        {
            ReleaseLink();
            return;
        }

        if (target is not SyncField<T> syncTarget)
        {
            throw new InvalidOperationException($"FieldDrive target must be a SyncField<{typeof(T).Name}>");
        }

        DriveTarget(syncTarget);
    }

    public void UpdateDrive()
    {
        if (!IsActive || Target == null || _valueSource == null)
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

    public void SetValue(T value)
    {
        if (!IsActive || Target == null)
        {
            return;
        }

        if (Target is SyncField<T> syncTarget)
        {
            if (LocalValueOnly)
            {
                syncTarget.SetDrivenValueLocal(value);
            }
            else
            {
                syncTarget.SetDrivenValue(value);
            }
        }
    }
}

public static class FieldDriveExtensions
{
    public static FieldDrive<T> CreateDrive<T>(this SyncField<T> target, Func<T> source)
    {
        var drive = new FieldDrive<T>(target.World);
        drive.DriveFrom(source);
        drive.DriveTarget(target);
        return drive;
    }
}
