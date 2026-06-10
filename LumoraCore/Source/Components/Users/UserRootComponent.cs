// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Base class for any user-scoped component that wants to be findable from
// UserRoot via GetRegisteredComponent<T>. Subscribes to ActiveUserRootChanged
// so re-registration happens automatically if the slot is reparented under a
// new user. - xlinka
public abstract class UserRootComponent : Component
{
    private UserRoot _registeredUserRoot = null!;

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
        {
            Slot.ActiveUserRootChanged += OnSlotActiveUserRootChanged;
            Register();
        }
    }

    public override void OnDestroy()
    {
        Unregister();
        if (Slot != null)
            Slot.ActiveUserRootChanged -= OnSlotActiveUserRootChanged;
        base.OnDestroy();
    }

    private void OnSlotActiveUserRootChanged(Slot slot)
    {
        if (Slot?.ActiveUserRoot == _registeredUserRoot)
            return;
        Unregister();
        Register();
    }

    private void Register()
    {
        var ur = Slot?.ActiveUserRoot;
        if (ur == null) return;
        _registeredUserRoot = ur;
        _registeredUserRoot.RegisterComponent(this);
    }

    private void Unregister()
    {
        if (_registeredUserRoot == null) return;
        _registeredUserRoot.UnregisterComponent(this);
        _registeredUserRoot = null!;
    }
}
