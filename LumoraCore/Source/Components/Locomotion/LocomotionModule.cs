// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Abstract base for locomotion modes. Each concrete module is a Component
// attached to the user's root slot. The controller keeps them in registration
// order and the active one is a sticky choice (menu / persisted), not picked
// by priority. Override CanActivate to gate eligibility (permission, VR-only).
// Movement logic lives in OnModuleUpdate, which the controller only calls
// while this module is active. - xlinka
[ComponentCategory("Users/Locomotion")]
public abstract class LocomotionModule : Component
{
    protected LocomotionController Owner { get; private set; } = null!;

    public bool IsActive => Owner != null && Owner.ActiveModule == this;

    // True when this module is eligible right now (e.g. VR-only when VR is live,
    // or permission-gated). The controller skips modules that return false.
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
