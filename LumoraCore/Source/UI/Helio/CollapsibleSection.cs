// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

/// <summary>
/// A clickable header that expands/collapses a content section (accordion row).
/// Toggles the "Content" child's active state and an optional "Indicator" child -
/// maps to an HTML details element.
/// </summary>
public sealed class CollapsibleSection : InteractionElement
{
    public readonly Sync<bool> Expanded;

    // Drives the content slot's active state.
    public FieldDrive<bool>? ContentVisual { get; private set; }
    // Optional: drives an indicator (e.g. an expanded-state arrow) active.
    public FieldDrive<bool>? IndicatorVisual { get; private set; }

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

    public override void OnAwake()
    {
        base.OnAwake();
        ContentVisual = new FieldDrive<bool>(World);
        IndicatorVisual = new FieldDrive<bool>(World);
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

    public override void OnDestroy()
    {
        ContentVisual?.Release();
        IndicatorVisual?.Release();
        ContentVisual = null;
        IndicatorVisual = null;
        base.OnDestroy();
    }

    // Rebind from the built child structure so a duplicated section keeps working.
    private void RebindVisuals()
    {
        var content = Slot?.FindChild("Content", recursive: false);
        if (content != null)
            ContentVisual?.DriveTarget(content.ActiveSelf);

        var indicator = Slot?.FindChild("Indicator", recursive: false)
                        ?? Slot?.FindChild("Header", recursive: false)?.FindChild("Indicator", recursive: false);
        if (indicator != null)
            IndicatorVisual?.DriveTarget(indicator.ActiveSelf);

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
        if (ContentVisual?.IsLinkValid == true)
            ContentVisual.SetValue(Expanded.Value);
        if (IndicatorVisual?.IsLinkValid == true)
            IndicatorVisual.SetValue(Expanded.Value);
    }
}
