// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core.Localization;
using LumoraEngine = Lumora.Core.Engine;

namespace Lumora.Source.Godot.UI;

// One line, bottom of the screen, while a world is still assembling itself.
//
// Opening a saved world or joining one returns immediately and holds the focus change until the world
// is actually somewhere you can stand. That is the right behaviour and it used to be completely
// silent: you pressed Open, nothing happened, and a few seconds later you were somewhere else. This
// says what is going on for those few seconds, and gets out of the way the instant it lands.
//
// It is a CanvasLayer, not world geometry, on purpose: the world it is describing is by definition not
// ready to hold anything, and the world you are standing in is not the one loading. -xlinka
public partial class WorldLoadOverlay : CanvasLayer
{
    // Under the dash composite (100) and the cursor (101), over the 3D view.
    private const int OverlayLayer = 99;

    private PanelContainer _pill = null!;
    private Label _label = null!;
    private string _appliedText = string.Empty;

    public override void _Ready()
    {
        Layer = OverlayLayer;
        Visible = false;

        var anchor = new Control
        {
            Name = "Anchor",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        anchor.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(anchor);

        var margin = new MarginContainer
        {
            Name = "Margin",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        margin.AddThemeConstantOverride("margin_bottom", 56);
        margin.GrowVertical = Control.GrowDirection.Begin;
        anchor.AddChild(margin);

        var center = new CenterContainer
        {
            Name = "Center",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(center);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.08f, 0.11f, 0.88f),
            BorderColor = new Color(0.30f, 0.55f, 0.95f, 0.95f),
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(1);

        _pill = new PanelContainer
        {
            Name = "Pill",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _pill.AddThemeStyleboxOverride("panel", style);
        center.AddChild(_pill);

        _label = new Label
        {
            Name = "Status",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeColorOverride("font_color", new Color(0.86f, 0.90f, 0.97f));
        _pill.AddChild(_label);
    }

    public void Tick(LumoraEngine? engine)
    {
        var world = engine?.WorldManager?.LoadingWorld;
        if (world == null)
        {
            if (Visible)
            {
                Visible = false;
                _appliedText = string.Empty;
            }
            return;
        }

        string name = string.IsNullOrEmpty(world.WorldName?.Value) ? world.Name : world.WorldName.Value;
        string text = "WorldLoad.Overlay".AsLocale("Loading {0}", name).Resolve()
            + "  -  " + world.LoadStateDescription.Resolve();

        // The state line changes as the backlog drains; the label write is the only cost here and it is
        // gated on the text actually differing.
        if (text != _appliedText)
        {
            _appliedText = text;
            _label.Text = text;
        }

        if (!Visible)
            Visible = true;
    }
}
