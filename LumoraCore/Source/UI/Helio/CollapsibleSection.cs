// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

// clickable header that expands/collapses a content section (accordion row). toggles the
// "Content" child's active state and an optional "Indicator" child.
[ComponentCategory("UI/Helio/Interaction")]
public sealed class CollapsibleSection : InteractionElement
{
    public readonly Sync<bool> Expanded;

    // Drives the content slot's active state. Declared member: the target replicates and saves.
    public readonly FieldDrive<bool> ContentVisual = new();
    // Optional: drives an indicator (e.g. an expanded-state arrow) active.
    public readonly FieldDrive<bool> IndicatorVisual = new();

    // Duplicable change action - see Button.Pressed.
    public readonly SyncDelegate<Action<CollapsibleSection, bool>> ChangeAction;

    // Folds when the press lets go instead of when it lands. See Button.ActOnRelease.
    public readonly Sync<bool> ActOnRelease;

    public event Action<CollapsibleSection, bool>? ExpandedChanged;

    public CollapsibleSection()
    {
        Expanded = new Sync<bool>(this, false);
        ChangeAction = new SyncDelegate<Action<CollapsibleSection, bool>>(this);
        ActOnRelease = new Sync<bool>(this, false);
    }

    protected override bool ActsOnPress => !ActOnRelease.Value && !InReleaseList();
    protected override bool IsClickControl => true;

    // A drag that starts here scrolls the list it sits in, the same as from a button.
    public override bool PassDragToParent(in float2 dragDelta)
        => System.Math.Abs(dragDelta.x) > DragPassThreshold || System.Math.Abs(dragDelta.y) > DragPassThreshold;

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

    protected override void OnSubmit(in UIInteractionContext context) => Toggle();

    // Flip the section and tell everyone who asked. Open to binding so a header row built out of a
    // Button can drive it: the hit scan hands a press to the deepest interactable under the pointer,
    // so a Button on a "Header" child takes the press and the section's own OnSubmit never sees it.
    // -xlinka
    [SyncMethod]
    public void Toggle()
    {
        Expanded.Value = !Expanded.Value;
        UpdateVisuals();
        ExpandedChanged?.Invoke(this, Expanded.Value);
        ChangeAction.Target?.Invoke(this, Expanded.Value);
    }

    // The header Button's action shape. Bound as a component method, so a duplicated section's header
    // toggles the COPY rather than reaching back to the original.
    [SyncMethod]
    public void OnHeaderPressed(Button button, UIInteractionContext context) => Toggle();

    private void UpdateVisuals()
    {
        if (ContentVisual.IsLinkValid)
            ContentVisual.SetValue(Expanded.Value);
        if (IndicatorVisual.IsLinkValid)
            IndicatorVisual.SetValue(Expanded.Value);
    }
}
