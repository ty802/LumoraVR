// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// One-line action card: a title on the left, the reason it cannot run right now on the right, and the
// whole card is the click target. Each action is its own type rather than one configurable preset with
// a label field, because popping a widget off the grid rebuilds it from its TYPE alone - a configured
// instance would come back blank and wired to nothing. -xlinka
public abstract class ActionWidgetPreset : HomeWidgetPreset
{
    protected abstract string Title { get; }

    // The one accent card on the screen. Everything else stays neutral, or nothing reads as primary.
    protected virtual bool Primary => false;

    // Null means the action can run. Anything else is shown on the card and disables it.
    protected virtual string? Unavailable => null;

    protected abstract void Invoke();

    private Text? _title;
    private Text? _hint;
    private Button? _button;
    private ColorDriver? _driver;
    private string? _lastHint;
    private bool _stateKnown;
    private bool _available = true;

    protected ActionWidgetPreset()
    {
        MinSize.Value = new float2(150f, 32f);
        PreferredSize.Value = new float2(250f, 42f);
        MaxSize.Value = new float2(560f, 120f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        _title = Label(root, "Title", Title, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(0.62f, 1f),
            new float2(Pad, 0f), float2.Zero, SemiboldFont);

        _hint = Label(root, "Hint", string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Right, new float2(0.62f, 0f), new float2(1f, 1f),
            float2.Zero, new float2(-Pad, 0f));

        _button = AddCardButton(Invoke);
        _driver = DriveCard(_button, DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed);
        Refresh();
    }

    protected override void Poll()
    {
        if (_button == null || _button.IsDestroyed)
            return;
        Refresh();
        ListingStyle.SetInteractable(_button, _available && !WidgetPanel.EditMode);
    }

    private void Refresh()
    {
        string? reason = Unavailable;
        bool available = reason == null;
        if (_stateKnown && available == _available && string.Equals(reason, _lastHint, System.StringComparison.Ordinal))
            return;
        _stateKnown = true;
        _available = available;
        _lastHint = reason;

        color fill = available ? (Primary ? DashTheme.Accent : DashTheme.Surface) : DashTheme.Field;
        color hover = available ? (Primary ? DashTheme.AccentHover : DashTheme.SurfaceHover) : DashTheme.Field;
        color pressed = available ? (Primary ? DashTheme.AccentPressed : DashTheme.SurfacePressed) : DashTheme.Field;
        SetCardRamp(_driver, fill, hover, pressed);

        if (_title != null && !_title.IsDestroyed)
            ListingStyle.SetTextColor(_title, available ? WidgetScreen.OnFill(fill) : DashTheme.TextMuted);
        if (_hint != null && !_hint.IsDestroyed)
            ListingStyle.SetText(_hint, reason ?? string.Empty);
        Repaint();
    }
}
