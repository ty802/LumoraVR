// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

public sealed class Radio : InteractionElement
{
    public readonly Sync<bool> IsChecked;
    public readonly Sync<string> Group;
    public FieldDrive<bool>? CheckVisual { get; private set; }

    public event Action<Radio, bool>? ValueChanged;

    public Radio()
    {
        IsChecked = new Sync<bool>(this, false);
        Group = new Sync<string>(this, string.Empty);
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
        if (IsChecked.Value) return;

        IsChecked.Value = true;
        UpdateCheckVisual();
        ValueChanged?.Invoke(this, true);

        var group = Group.Value;
        if (string.IsNullOrEmpty(group)) return;

        foreach (var radio in context.Canvas.Slot.GetComponentsInChildren<Radio>(true))
        {
            if (ReferenceEquals(radio, this) || radio.Group.Value != group || !radio.IsChecked.Value)
            {
                continue;
            }

            radio.IsChecked.Value = false;
            radio.UpdateCheckVisual();
            radio.ValueChanged?.Invoke(radio, false);
        }
    }

    private void UpdateCheckVisual()
    {
        if (CheckVisual?.IsLinkValid == true)
        {
            CheckVisual.SetValue(IsChecked.Value);
        }
    }
}
