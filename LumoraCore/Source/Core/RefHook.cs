// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

public delegate void RefSetHookDelegate<T>(SyncRef<T> syncRef, T value) where T : class, IWorldElement;

public class RefHook<T> : LinkBase<SyncRef<T>> where T : class, IWorldElement
{
    public RefHook()
    {
    }

    public RefHook(IWorldElement? owner) : base(owner)
    {
    }

    public RefSetHookDelegate<T>? RefSetHook { get; set; }

    public override bool IsDriving => false;

    public override bool IsHooking => RefSetHook != null;

    // The member itself stays alive, it is disposed with its owner.
    public void Release()
    {
        ReleaseLink();
    }
}

// Declare it as a readonly member; the link target replicates and persists like any other ref.
public class DriveRef<T> : RefHook<T> where T : class, IWorldElement
{
    public DriveRef()
    {
    }

    public DriveRef(IWorldElement? owner) : base(owner)
    {
    }

    public override bool IsDriving => true;

    public override bool IsModificationAllowed => true;

    // Passing null releases the current target.
    public void DriveTarget(SyncRef<T>? target)
    {
        Target = target!;
    }

    public void SetValue(T? value)
    {
        var target = Target;
        if (!IsLinkValid || target == null)
        {
            return;
        }

        var id = value?.ReferenceID ?? RefID.Null;
        if (LocalValueOnly)
        {
            target.SetDrivenValueLocal(id);
        }
        else
        {
            target.SetDrivenValue(id);
        }
    }

    // See FieldDrive.LocalValueOnly: skip sync generation for a drive every peer computes itself.
    public bool LocalValueOnly { get; set; }
}
