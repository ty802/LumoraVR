// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;

namespace Helio.UI;

public abstract class UIComputeComponent : UIComponent
{
    // opt in if the component needs async work before layout (text shaping etc) - xlinka
    public virtual bool RequiresPreLayoutCompute => false;

    public override void OnAwake()
    {
        base.OnAwake();
        RectTransform = Slot.GetComponent<RectTransform>();
        RectTransform?.NotifyComponentsChanged();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var rt = RectTransform;
        if (rt != null && !rt.IsDestroyed)
        {
            FlagChanges(rt);
        }
    }

    public override void OnEnabled()
    {
        base.OnEnabled();
        RectTransform?.NotifyComponentsChanged();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        RectTransform?.NotifyComponentsChanged();
    }

    public override void OnDestroying()
    {
        if (Slot != null && !Slot.IsRemoved)
        {
            Slot.GetComponent<RectTransform>()?.NotifyComponentsChanged();
        }
        base.OnDestroying();
    }

    protected abstract void FlagChanges(RectTransform rect);

    public abstract void PrepareCompute();

    public virtual ValueTask PreLayoutCompute() => default;
}
