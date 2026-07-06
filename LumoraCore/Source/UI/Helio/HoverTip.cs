// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI;

/// <summary>
/// Shows a "Tip" child slot while a sibling <see cref="InteractionElement"/> on the
/// same slot is hovered (a tooltip). Composes with any existing interactable
/// (Button, Checkbox, ...) - maps to an HTML title/tooltip.
/// </summary>
public sealed class HoverTip : UIComponent
{
    // Drives the tip slot's active state.
    public FieldDrive<bool>? TipVisual { get; private set; }

    private InteractionElement? _source;

    public override void OnAwake()
    {
        base.OnAwake();
        TipVisual = new FieldDrive<bool>(World);
    }

    public override void OnStart()
    {
        base.OnStart();

        var tip = Slot?.FindChild("Tip", recursive: false);
        if (tip != null)
        {
            TipVisual?.DriveTarget(tip.ActiveSelf);
            TipVisual?.SetValue(false); // hidden until hovered
        }

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
        TipVisual?.Release();
        TipVisual = null;
        base.OnDestroy();
    }

    private void OnHoverEnter(UIInteractionContext context) => SetTip(true);
    private void OnHoverExit(UIInteractionContext context) => SetTip(false);

    private void SetTip(bool visible)
    {
        if (TipVisual?.IsLinkValid == true)
            TipVisual.SetValue(visible);
    }
}
