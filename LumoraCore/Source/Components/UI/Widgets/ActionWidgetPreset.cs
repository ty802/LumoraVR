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

    // For a card whose action cannot be taken back: it waits for the press to let go over it.
    protected virtual bool CardActsOnRelease => false;

    // Null means the action can run. Anything else is shown on the card and disables it.
    protected virtual string? Unavailable => null;

    // Bundled icon name for the tile at the left of the card, or none for a plain card.
    protected virtual string? Icon => null;
    protected virtual color IconTint => DashTheme.Accent;

    protected abstract void Invoke();

    // Under this width the card is a tile: the reason it cannot run goes (the greyed fill still says it)
    // and the title takes the whole width beside the icon.
    private const float CompactWidth = 210f;

    private Text? _title;
    private Text? _hint;
    private Button? _button;
    private ColorDriver? _driver;
    private string? _lastHint;
    private string? _lastTitle;
    private bool _stateKnown;
    private bool _available = true;
    private bool? _compact;
    private float _titleLeft = Pad;

    public override string Tip => Unavailable is { } why ? Title + ": " + why : Title;

    protected ActionWidgetPreset()
    {
        MinSize.Value = new float2(150f, 32f);
        PreferredSize.Value = new float2(250f, 42f);
        MaxSize.Value = new float2(560f, 120f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        if (IconOnly)
        {
            IconOnlyGlyphAt(root, Icon ?? "plus");
            _button = AddCardButton(Press);
            _button.ActOnRelease.Value = CardActsOnRelease;
            _driver = DriveCard(_button, color.Transparent, DashTheme.GlassCardHover, DashTheme.GlassCardPressed);
            Refresh();
            return;
        }
        float titleLeft = Pad;
        string? icon = Icon;
        if (icon != null)
        {
            IconTileAt(root, icon, IconTint, Pad - 6f, IconTile);
            titleLeft = IconTitleLeft;
        }
        _titleLeft = titleLeft;
        _title = Label(root, "Title", Title, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(0.62f, 1f),
            new float2(titleLeft, 0f), float2.Zero, SemiboldFont);
        _title.AutoSizeHorizontal.Value = true;
        _title.AutoSizeMin.Value = 11f;

        _hint = Label(root, "Hint", string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Right, new float2(0.62f, 0f), new float2(1f, 1f),
            float2.Zero, new float2(-Pad, 0f));

        _button = AddCardButton(Press);
        _button.ActOnRelease.Value = CardActsOnRelease;
        _driver = DriveCard(_button, DashTheme.GlassCard, DashTheme.GlassCardHover, DashTheme.GlassCardPressed);
        Refresh();
    }

    // Repainted on the press as well as on the poll: a card whose press switches screens is hidden before
    // the next poll, and would come back still showing the old state.
    private void Press()
    {
        if (!IsExecutionAllowed)
            return;
        Invoke();
        if (!IsDestroyed)
            Refresh();
    }

    protected override void Poll()
    {
        if (_button == null || _button.IsDestroyed)
            return;
        Refresh();
        ListingStyle.SetInteractable(_button, _available && !WidgetPanel.EditMode);
        ApplyCompact();
    }

    private void ApplyCompact()
    {
        float width = Slot.GetComponent<RectTransform>()?.LocalComputeRect.width ?? 0f;
        if (width <= 1f || _title == null || _title.IsDestroyed)
            return;
        bool compact = width < CompactWidth;
        if (_compact == compact)
            return;
        _compact = compact;
        var rect = SettingsUI.Rect(_title.Slot);
        SettingsUI.SetAnchors(rect, float2.Zero, new float2(compact ? 1f : 0.62f, 1f));
        SettingsUI.SetOffsets(rect, new float2(_titleLeft, 0f), new float2(compact ? -Pad * 0.5f : 0f, 0f));
        if (_hint != null && !_hint.IsDestroyed)
            SetActive(_hint.Slot, !compact);
    }

    private void Refresh()
    {
        string? reason = Unavailable;
        // Read every poll: a card whose title says which way the press goes (on, off) changes it.
        string title = Title;
        bool available = reason == null;
        if (_stateKnown && available == _available && string.Equals(reason, _lastHint, System.StringComparison.Ordinal)
            && string.Equals(title, _lastTitle, System.StringComparison.Ordinal))
            return;
        _stateKnown = true;
        _available = available;
        _lastHint = reason;
        _lastTitle = title;

        if (IconOnly)
        {
            PaintIcon(_driver, false, available);
            Repaint();
            return;
        }

        color fill = available ? (Primary ? DashTheme.Accent : DashTheme.GlassCard) : DashTheme.Field;
        color hover = available ? (Primary ? DashTheme.AccentHover : DashTheme.GlassCardHover) : DashTheme.Field;
        color pressed = available ? (Primary ? DashTheme.AccentPressed : DashTheme.GlassCardPressed) : DashTheme.Field;
        SetCardRamp(_driver, fill, hover, pressed);

        if (_title != null && !_title.IsDestroyed)
        {
            ListingStyle.SetText(_title, title);
            ListingStyle.SetTextColor(_title, available ? WidgetScreen.OnFill(fill) : DashTheme.TextMuted);
        }
        if (_hint != null && !_hint.IsDestroyed)
            ListingStyle.SetText(_hint, reason ?? string.Empty);
        Repaint();
    }
}
