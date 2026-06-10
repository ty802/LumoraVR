// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Abstract base for locomotion modes. Each concrete module is a Component
// attached to the user's root slot, picked by LocomotionController based on
// priority and CanActivate at runtime. Override Priority + CanActivate to
// gate when the module is eligible (VR module checks tracking, etc).
// Movement logic lives in OnModuleUpdate, which the controller only calls
// while this module is active. - xlinka
[ComponentCategory("Users/Locomotion")]
public abstract class LocomotionModule : Component
{
    protected LocomotionController Owner { get; private set; } = null!;

    public bool IsActive => Owner != null && Owner.ActiveModule == this;

    // Higher wins when controller picks the default module. VR=100, Desktop=50, Null=0.
    public virtual int Priority => 0;

    // True when this module is eligible right now (e.g. VR-only when VR is live).
    public virtual bool CanActivate() => true;

    public virtual string DisplayName => GetType().Name;

    public void ActivateInternal(LocomotionController owner)
    {
        Owner = owner;
        OnActivated();
    }

    public void DeactivateInternal()
    {
        OnDeactivated();
        Owner = null!;
    }

    protected virtual void OnActivated() { }
    protected virtual void OnDeactivated() { }
    public abstract void OnModuleUpdate(float delta);

    public override void OnDestroy()
    {
        Owner?.UnregisterModule(this);
        base.OnDestroy();
    }
}
