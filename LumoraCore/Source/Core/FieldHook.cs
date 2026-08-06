// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

public delegate void HookFieldSetter<T>(SyncField<T> field, T value);

// Declare it as a readonly member on the owning worker like any other sync member; the target ref
// replicates and persists, and the interception delegate is wired in code on each peer.
public class FieldHook<T> : LinkBase<IField<T>>
{
    private HookFieldSetter<T>? _fieldHook;

    public FieldHook()
    {
    }

    public FieldHook(IWorldElement? owner) : base(owner)
    {
    }

    public override bool IsDriving => false;

    // A hook only intercepts once a delegate has been installed.
    public override bool IsHooking => HookSetup;

    public HookFieldSetter<T>? ValueSetHook => _fieldHook;

    public bool HookSetup => _fieldHook != null;

    public SyncField<T>? TargetField => Target as SyncField<T>;

    public bool IsActive => IsLinkValid;

    // Can only be called once.
    public void SetupValueSetHook(HookFieldSetter<T> hook)
    {
        if (HookSetup)
        {
            throw new InvalidOperationException("Hook method can be setup only once!");
        }
        _fieldHook = hook;
    }

    public void HookTarget(IField<T>? target)
    {
        Target = target!;
    }

    // The member itself stays alive, it is disposed with its owner.
    public void Release()
    {
        ReleaseLink();
    }
}
