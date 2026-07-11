// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

public sealed class Radio : InteractionElement
{
    public readonly Sync<bool> IsChecked;
    public readonly Sync<string> Group;
    // drives the dot's active state. declared member, so the target replicates and saves.
    public readonly FieldDrive<bool> CheckVisual = new();

    public event Action<Radio, bool>? ValueChanged;

    public Radio()
    {
        IsChecked = new Sync<bool>(this, false);
        Group = new Sync<string>(this, string.Empty);
    }

    public override void OnStart()
    {
        base.OnStart();
        // Default the dot drive from the built child structure when nothing named a target yet. The
        // link itself replicates and persists now, so a peer or a loaded save arrives with the target
        // already set and this no-ops; it only fills in for a radio built by hand or duplicated. -xlinka
        if (CheckVisual.ShouldApplyDefault)
        {
            var dot = Slot?.FindChild("Ring", recursive: false)?.FindChild("Dot", recursive: false);
            if (dot != null)
                CheckVisual.DriveTarget(dot.ActiveSelf);
        }
        UpdateCheckVisual();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateCheckVisual();
    }

    public void SetCheckVisual(IField<bool> target)
    {
        CheckVisual.DriveTarget(target);
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
        if (CheckVisual.IsLinkValid)
        {
            CheckVisual.SetValue(IsChecked.Value);
        }
    }
}
