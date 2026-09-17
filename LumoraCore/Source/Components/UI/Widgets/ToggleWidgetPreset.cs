// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// One-line switch card, drawn as a plain card rather than a card with a control on it: the title
// carries the state and the fill carries it again, so the whole tile is one press with nothing small
// to aim at in VR. The value is polled rather than cached, because these flags are also flipped from
// Settings and from the toggle's own twin on a popped-out panel. -xlinka
public abstract class ToggleWidgetPreset : HomeWidgetPreset
{
    protected abstract string Title { get; }

    protected abstract bool Value { get; set; }

    // Edit mode kills every click target inside a widget so drags reach the grid instead. The card
    // that TURNS EDIT MODE OFF cannot follow that rule or there is no way back out of it. -xlinka
    protected virtual bool LiveInEditMode => false;

    protected virtual string? Icon => null;
    protected virtual color IconTint => DashTheme.Accent;

    private Text? _title;
    private Button? _cardButton;
    private ColorDriver? _driver;
    private bool _last;
    private bool _known;

    protected ToggleWidgetPreset()
    {
        MinSize.Value = new float2(180f, 32f);
        PreferredSize.Value = new float2(250f, 42f);
        MaxSize.Value = new float2(560f, 120f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        float titleLeft = Pad;
        string? icon = Icon;
        if (icon != null)
        {
            IconTileAt(root, icon, IconTint, Pad - 6f, IconTile);
            titleLeft = IconTitleLeft;
        }
        _title = Label(root, "Title", TitleFor(Value), DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(1f, 1f),
            new float2(titleLeft, 0f), new float2(-Pad, 0f), SemiboldFont);

        _cardButton = AddCardButton(Flip);
        _driver = DriveCard(_cardButton, DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed);
        _known = false;
        Refresh();
    }

    protected override void Poll()
    {
        if (_cardButton == null || _cardButton.IsDestroyed)
            return;
        Refresh();
        ListingStyle.SetInteractable(_cardButton, LiveInEditMode || !WidgetPanel.EditMode);
    }

    private void Flip()
    {
        Value = !Value;
        Refresh();
    }

    private void Refresh()
    {
        bool on = Value;
        if (_known && on == _last)
            return;
        _known = true;
        _last = on;

        color fill = on ? DashTheme.Accent : DashTheme.Surface;
        SetCardRamp(_driver, fill,
            on ? DashTheme.AccentHover : DashTheme.SurfaceHover,
            on ? DashTheme.AccentPressed : DashTheme.SurfacePressed);
        if (_title != null && !_title.IsDestroyed)
        {
            ListingStyle.SetText(_title, TitleFor(on));
            ListingStyle.SetTextColor(_title, WidgetScreen.OnFill(fill));
        }
        Repaint();
    }

    private string TitleFor(bool on)
        => Title + ": " + (on ? "Widget.Toggle.On".AsLocale("On") : "Widget.Toggle.Off".AsLocale("Off")).Resolve();
}

// Stop the dash snapping in front of the view every frame, so it can be placed and left there.
[ComponentCategory("Hidden")]
public sealed class FreeformDashWidgetPreset : ToggleWidgetPreset
{
    protected override string Title => "Freeform Dash";
    protected override string? Icon => "freeform";
    protected override color IconTint => DashTheme.Warning;

    protected override bool Value
    {
        get => UserspaceDashboard.LocalInstance?.Freeform.Value ?? false;
        set => UserspaceDashboard.LocalInstance?.SetFreeform(value);
    }
}

// Show the grid lines and make every widget draggable, here and on the popped-out panels.
[ComponentCategory("Hidden")]
public sealed class EditWidgetsWidgetPreset : ToggleWidgetPreset
{
    protected override string Title => "Edit Widgets";
    protected override string? Icon => "edit-widgets";
    protected override color IconTint => DashTheme.Accent;

    protected override bool LiveInEditMode => true;

    protected override bool Value
    {
        get => WidgetPanel.EditMode;
        set => WidgetPanel.EditMode = value;
    }
}
