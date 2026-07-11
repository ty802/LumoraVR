// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

// clickable header that expands/collapses a content section (accordion row). toggles the
// "Content" child's active state and an optional "Indicator" child.
public sealed class CollapsibleSection : InteractionElement
{
    public readonly Sync<bool> Expanded;

    // Drives the content slot's active state. Declared member: the target replicates and saves.
    public readonly FieldDrive<bool> ContentVisual = new();
    // Optional: drives an indicator (e.g. an expanded-state arrow) active.
    public readonly FieldDrive<bool> IndicatorVisual = new();

    // Duplicable change action - see Button.Pressed.
    public readonly SyncDelegate<Action<CollapsibleSection, bool>> ChangeAction;

    public event Action<CollapsibleSection, bool>? ExpandedChanged;

    public CollapsibleSection()
    {
        Expanded = new Sync<bool>(this, false);
        ChangeAction = new SyncDelegate<Action<CollapsibleSection, bool>>(this);
    }

    public void SetAction(Action<CollapsibleSection, bool>? action)
    {
        if (action == null)
            return;
        if (action.Target is IWorldElement)
            ChangeAction.Target = action;
        else
            ExpandedChanged += action;
    }

    public override void OnStart()
    {
        base.OnStart();
        RebindVisuals();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateVisuals();
    }

    // Default the visual drives from the built child structure, but only where nothing named a target:
    // a link that arrived from a save or a peer already knows what it drives.
    private void RebindVisuals()
    {
        if (ContentVisual.ShouldApplyDefault)
        {
            var content = Slot?.FindChild("Content", recursive: false);
            if (content != null)
                ContentVisual.DriveTarget(content.ActiveSelf);
        }

        if (IndicatorVisual.ShouldApplyDefault)
        {
            var indicator = Slot?.FindChild("Indicator", recursive: false)
                            ?? Slot?.FindChild("Header", recursive: false)?.FindChild("Indicator", recursive: false);
            if (indicator != null)
                IndicatorVisual.DriveTarget(indicator.ActiveSelf);
        }

        UpdateVisuals();
    }

    protected override void OnSubmit(in UIInteractionContext context)
    {
        Expanded.Value = !Expanded.Value;
        UpdateVisuals();
        ExpandedChanged?.Invoke(this, Expanded.Value);
        ChangeAction.Target?.Invoke(this, Expanded.Value);
    }

    private void UpdateVisuals()
    {
        if (ContentVisual.IsLinkValid)
            ContentVisual.SetValue(Expanded.Value);
        if (IndicatorVisual.IsLinkValid)
            IndicatorVisual.SetValue(Expanded.Value);
    }
}
