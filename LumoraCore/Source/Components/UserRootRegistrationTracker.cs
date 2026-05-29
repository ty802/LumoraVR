// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// Drop-in helper for components that can't use UserRootComponent as a base
// (typically ImplementableComponent subclasses that already need a hook).
// Construct one with the owning component, call Attach() in OnAwake and
// Detach() in OnDestroy. Re-registers automatically when ActiveUserRoot
// changes. - xlinka
public sealed class UserRootRegistrationTracker
{
    private readonly Component _component;
    private UserRoot _registered;

    public UserRootRegistrationTracker(Component component)
    {
        _component = component;
    }

    public void Attach()
    {
        if (_component?.Slot == null) return;
        _component.Slot.ActiveUserRootChanged += OnChanged;
        Refresh();
    }

    public void Detach()
    {
        if (_component?.Slot != null)
            _component.Slot.ActiveUserRootChanged -= OnChanged;
        Unregister();
    }

    private void OnChanged(Slot _) => Refresh();

    private void Refresh()
    {
        var current = _component?.Slot?.ActiveUserRoot;
        if (current == _registered) return;
        Unregister();
        if (current != null)
        {
            _registered = current;
            current.RegisterComponent(_component);
        }
    }

    private void Unregister()
    {
        if (_registered == null) return;
        _registered.UnregisterComponent(_component);
        _registered = null;
    }
}
