// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

public sealed class Checkbox : InteractionElement
{
    public readonly Sync<bool> IsChecked;
    public FieldDrive<bool>? CheckVisual { get; private set; }

    public event Action<Checkbox, bool>? ValueChanged;

    public Checkbox()
    {
        IsChecked = new Sync<bool>(this, false);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        CheckVisual = new FieldDrive<bool>(World);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateCheckVisual();
    }

    public override void OnDestroy()
    {
        CheckVisual?.Release();
        CheckVisual = null;
        base.OnDestroy();
    }

    public void SetCheckVisual(IField<bool> target)
    {
        CheckVisual?.DriveTarget(target);
        UpdateCheckVisual();
    }

    protected override void OnSubmit(in UIInteractionContext context)
    {
        IsChecked.Value = !IsChecked.Value;
        UpdateCheckVisual();
        ValueChanged?.Invoke(this, IsChecked.Value);
    }

    private void UpdateCheckVisual()
    {
        if (CheckVisual?.IsLinkValid == true)
        {
            CheckVisual.SetValue(IsChecked.Value);
        }
    }
}
