// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI;

// shows a "Tip" child slot while a sibling InteractionElement on the same slot is hovered.
// composes with any existing interactable (Button, Checkbox, ...).
public sealed class HoverTip : UIComponent
{
    // Drives the tip slot's active state. Declared member: the target replicates and saves.
    public readonly FieldDrive<bool> TipVisual = new();

    private InteractionElement? _source;

    public override void OnStart()
    {
        base.OnStart();

        // Only when nothing set the target: a loaded or replicated link already names its tip.
        if (TipVisual.ShouldApplyDefault)
        {
            var tip = Slot?.FindChild("Tip", recursive: false);
            if (tip != null)
                TipVisual.DriveTarget(tip.ActiveSelf);
        }
        TipVisual.SetValue(false); // hidden until hovered

        _source = Slot?.GetComponent<InteractionElement>();
        if (_source != null)
        {
            _source.HoverEntered += OnHoverEnter;
            _source.HoverExited += OnHoverExit;
        }
    }

    public override void OnDestroy()
    {
        if (_source != null)
        {
            _source.HoverEntered -= OnHoverEnter;
            _source.HoverExited -= OnHoverExit;
            _source = null;
        }
        base.OnDestroy();
    }

    private void OnHoverEnter(UIInteractionContext context) => SetTip(true);
    private void OnHoverExit(UIInteractionContext context) => SetTip(false);

    private void SetTip(bool visible)
    {
        if (TipVisual.IsLinkValid)
            TipVisual.SetValue(visible);
    }
}
