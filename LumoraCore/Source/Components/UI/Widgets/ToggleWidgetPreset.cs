// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// One-line switch card: title on the left, the settings screen's switch on the right so both places
// read the same. The value is polled rather than cached, because these flags are also flipped from
// Settings and from the toggle's own twin on a popped-out panel. -xlinka
public abstract class ToggleWidgetPreset : HomeWidgetPreset
{
    // Track plus the "On"/"Off" word the switch draws beside it.
    private const float SwitchColumn = ToggleSwitch.TrackWidth + 60f;

    protected abstract string Title { get; }

    protected abstract bool Value { get; set; }

    // Edit mode kills every click target inside a widget so drags reach the grid instead. The switch
    // that TURNS EDIT MODE OFF cannot follow that rule or there is no way back out of it. -xlinka
    protected virtual bool LiveInEditMode => false;

    private ToggleSwitch? _switch;
    private Button? _cardButton;
    private Button? _switchButton;
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
        Label(root, "Title", Title, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(1f, 1f),
            new float2(Pad, 0f), new float2(-(Pad + SwitchColumn + 10f), 0f), SemiboldFont);

        var host = SettingsUI.Child(root, "Switch", new float2(1f, 0f), new float2(1f, 1f),
            new float2(-(Pad + SwitchColumn), 0f), new float2(-Pad, 0f));
        _switch = ToggleSwitch.Build(host, BodyFont);

        _switchButton = host.AttachComponent<Button>();
        _switchButton.Clicked += (_, _) => Flip();

        _cardButton = AddCardButton(Flip);
        DriveCard(_cardButton, DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed);
        Refresh();
    }

    protected override void Poll()
    {
        if (_switch == null)
            return;
        Refresh();
        ListingStyle.SetInteractable(_cardButton, !WidgetPanel.EditMode);
        ListingStyle.SetInteractable(_switchButton, LiveInEditMode || !WidgetPanel.EditMode);
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
        _switch?.SetValue(on, true);
        Repaint();
    }
}

// Stop the dash snapping in front of the view every frame, so it can be placed and left there.
[ComponentCategory("Hidden")]
public sealed class FreeformDashWidgetPreset : ToggleWidgetPreset
{
    protected override string Title => "Freeform Dash";

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

    protected override bool LiveInEditMode => true;

    protected override bool Value
    {
        get => WidgetPanel.EditMode;
        set => WidgetPanel.EditMode = value;
    }
}
