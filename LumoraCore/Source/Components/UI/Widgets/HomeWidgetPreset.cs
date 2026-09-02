// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Shared plumbing for the widgets on the Home grid. They are placed by HomeScreen, but a widget can be
// dragged off the grid onto its own world panel, and there nobody hands it a font or a corner sprite -
// so everything it needs is resolved from the local dashboard instead of being passed in. A Text with a
// null font renders nothing at all, which is what a popped-out widget used to look like. -xlinka
public abstract class HomeWidgetPreset : WidgetPreset
{
    protected const float Pad = 14f;

    private float _pollTimer;

    protected HomeWidgetPreset()
    {
        Background.Value = DashTheme.Surface;
        BorderColor.Value = DashTheme.Outline;
        CornerRadius.Value = DashTheme.RadiusCard;
        // Two cells is what the built-in header pills get, and a widget dropped back onto the top bar is
        // rebuilt from defaults, so one cell would dock it as a square stub.
        GridWidth.Value = 2;
        MinSize.Value = new float2(160f, 36f);
        PreferredSize.Value = new float2(260f, 44f);
        MaxSize.Value = new float2(960f, 480f);
    }

    // The dash in THIS world. A popped-out panel and the dash both live in userspace, so the local
    // instance is the one to ask; the world check is there because a cross-world asset target is
    // rejected and would leave every label unfonted rather than fail loudly.
    protected Dashboard? Dash
    {
        get
        {
            var dash = UserspaceDashboard.LocalInstance?.Dashboard;
            return dash != null && !dash.IsDestroyed && ReferenceEquals(dash.World, World) ? dash : null;
        }
    }

    protected IAssetProvider<FontSet>? BodyFont => Dash?.Font.Target;
    protected IAssetProvider<FontSet>? SemiboldFont => Dash?.FontSemibold.Target ?? BodyFont;
    protected IAssetProvider<FontSet>? BoldFont => Dash?.FontBold.Target ?? BodyFont;

    // True while this widget sits on a dashboard grid rather than on its own world panel. Keyboard
    // routing only exists on the dash, so the account form asks this before offering input.
    protected bool OnDashboard => Slot.GetComponentInParents<Dashboard>() != null;

    protected override void OnPreBuild()
    {
        // Only the dash can cut a nine-slice sprite for a given radius, and a popped-out widget was
        // never handed one, so it would come up as a hard-cornered box next to rounded siblings.
        if (BackgroundSprite.Target != null)
            return;
        var dash = Dash;
        if (dash != null)
            BackgroundSprite.Target = dash.RoundedSpriteFor(CornerRadius.Value);
    }

    protected void Repaint()
    {
        if (IsDestroyed)
            return;
        Slot.GetComponentInParents<Canvas>()?.MarkDirty();
    }

    protected static void SetActive(Slot? slot, bool active)
    {
        if (slot != null && !slot.IsDestroyed)
            ListingStyle.SetActive(slot, active);
    }

    protected static World? FocusedWorld
    {
        get
        {
            var world = Engine.Current?.WorldManager?.FocusedWorld;
            return world != null && !world.IsDestroyed ? world : null;
        }
    }

    // The local home world is the one the manager itself falls back to, matched on the same name it
    // uses. It is the "you are already there" case for both Go Home and Leave World.
    protected static bool IsLocalHome(World? world)
        => world != null && !world.IsDestroyed && world.WorldName?.Value == "LocalHome";

    // Click target for the whole card, on the WIDGET slot rather than the content child. Edit mode
    // switches it off (GateForEdit), and with nothing live inside the card the grid underneath gets the
    // pointer, which is how a grab on the card becomes a pick-up instead of a click.
    protected Button AddCardButton(Action onClick)
    {
        var button = Slot.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();
        return button;
    }

    // Hover/press ramp on the card's own background. All four states are written: AddColorDriver derives
    // its ramp from the base color, which lands somewhere arbitrary for near-black fills. DisabledColor
    // is deliberately the normal color - a widget builds while its screen slot is still inactive, so the
    // first Apply paints the disabled entry and nothing repaints it until you hover the card. Widgets
    // that have an unavailable state repaint the whole ramp instead. -xlinka
    protected ColorDriver? DriveCard(Button button, in color normal, in color hover, in color pressed)
    {
        var background = CardBackground;
        if (background == null)
            return null;
        var driver = button.AddColorDriver(background.Tint, normal, InteractionColorMode.Direct);
        SetCardRamp(driver, normal, hover, pressed);
        return driver;
    }

    protected static void SetCardRamp(ColorDriver? driver, in color normal, in color hover, in color pressed)
    {
        if (driver == null || driver.IsDestroyed)
            return;
        driver.NormalColor.Value = normal;
        driver.HighlightColor.Value = hover;
        driver.PressedColor.Value = pressed;
        driver.DisabledColor.Value = normal;
        driver.Apply();
    }

    // Everything you can click inside a widget goes dead while the grid is in edit mode. Child slots are
    // hit-tested after their parent, so a field button would otherwise steal the drag that is meant to
    // pick the widget up. -xlinka
    protected static void GateForEdit(InteractionElement? element)
        => ListingStyle.SetInteractable(element, !WidgetPanel.EditMode);

    // Widgets read live state (which world has focus, whether the directory answers) rather than being
    // pushed at. That is a poll, and a poll on a UI card belongs on its own slow clock: at frame rate the
    // world lookups alone were handing the collector a delegate every frame for a label that changes once
    // a session. -xlinka
    protected virtual float PollInterval => 0.25f;

    protected virtual void Poll()
    {
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!Slot.IsActive)
            return;
        _pollTimer += delta;
        if (_pollTimer < PollInterval)
            return;
        _pollTimer = 0f;
        Poll();
    }

    protected Text Label(Slot parent, string name, string content, float size, in color tint,
        TextHorizontalAlignment align, in float2 anchorMin, in float2 anchorMax, in float2 offsetMin, in float2 offsetMax,
        IAssetProvider<FontSet>? font = null)
    {
        var text = SettingsUI.Label(parent, name, font ?? BodyFont, size, tint, align,
            anchorMin, anchorMax, offsetMin, offsetMax);
        text.Content.Value = content;
        return text;
    }

    // A band of fixed height measured DOWN from the top edge, which is how every widget body here is
    // laid out: the width follows the card, the rows do not move when it resizes.
    protected static Slot Row(Slot parent, string name, float top, float height, float left, float right)
        => SettingsUI.Child(parent, name, new float2(0f, 1f), new float2(1f, 1f),
            new float2(left, -(top + height)), new float2(-right, -top));
}
