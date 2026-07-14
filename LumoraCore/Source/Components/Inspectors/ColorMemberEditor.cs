// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Globalization;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// color/colorHDR members: float fields are addressed via leaf paths r/g/b/a on top of this editor's own path
public class ColorMemberEditor : MemberEditor
{
    private readonly SyncRef<Image> _swatch;
    private readonly SyncRef<Image> _alphaSwatch;

    public ColorMemberEditor()
    {
        _swatch = new SyncRef<Image>(this);
        _alphaSwatch = new SyncRef<Image>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        string basePath = string.IsNullOrEmpty(MemberPath.Value) ? "" : MemberPath.Value + ".";
        foreach (var channel in new[] { "r", "g", "b", "a" })
        {
            ui.PushStyle();
            ui.FlexibleWidth(1f);
            var channelSlot = ui.Next(channel.ToUpperInvariant());
            channelSlot.AttachComponent<Helio.UI.Layout.HorizontalLayout>();
            ui.NestInto(channelSlot);
            var editor = channelSlot.AttachComponent<PrimitiveMemberEditor>();
            editor.Setup(Field!, basePath + channel, ui);
            ui.NestOut();
            ui.PopStyle();
        }

        // CIRCULAR split swatch: left half = the hue solid, right half = the color WITH its real alpha
        // over a checkerboard, the whole thing stencil-clipped to a circle (fully-rounded shape texture
        // stamped into the stencil). Pressing anywhere on it opens the picker.
        ui.PushStyle();
        ui.MinWidth(30f);
        ui.PreferredWidth(30f);
        ui.FlexibleWidth(0f);
        var swatchSlot = ui.Next("Swatch");
        swatchSlot.AttachComponent<Helio.UI.Layout.HorizontalLayout>().ForceExpandHeight.Value = true;
        swatchSlot.AttachComponent<Button>().SetAction(OnSwatchPressed);

        var circleTex = swatchSlot.AttachComponent<Lumora.Core.Assets.RoundedRectTextureProvider>();
        circleTex.Size.Value = 64;
        circleTex.Radius.Value = 32; // radius = size/2 -> circle
        var circleShape = swatchSlot.AttachComponent<Image>();
        circleShape.Texture.Target = circleTex;
        var circleMask = swatchSlot.AttachComponent<Mask>();
        circleMask.StencilMasking.Value = true;
        circleMask.ShowMaskGraphic.Value = false;

        var solidSlot = swatchSlot.AddSlot("Solid");
        solidSlot.AttachComponent<RectTransform>();
        var solidLE = solidSlot.AttachComponent<Helio.UI.Layout.LayoutElement>();
        solidLE.FlexibleWidth.Value = 1f;
        _swatch.Target = solidSlot.AttachComponent<Image>();

        var alphaSlot = swatchSlot.AddSlot("Alpha");
        alphaSlot.AttachComponent<RectTransform>();
        var alphaLE = alphaSlot.AttachComponent<Helio.UI.Layout.LayoutElement>();
        alphaLE.FlexibleWidth.Value = 1f;
        var checker = alphaSlot.AttachComponent<Lumora.Core.Assets.CheckerTextureProvider>();
        checker.ColorA.Value = new color(0.20f, 0.20f, 0.24f, 1f);
        checker.ColorB.Value = new color(0.12f, 0.12f, 0.15f, 1f);
        checker.CellSize.Value = 6;
        var checkerImage = alphaSlot.AttachComponent<RawImage>();
        checkerImage.Texture.Target = checker;
        var alphaTint = alphaSlot.AddSlot("Tint");
        InspectorUI.FillParent(alphaTint.AttachComponent<RectTransform>());
        _alphaSwatch.Target = alphaTint.AttachComponent<Image>();
        ui.PopStyle();
    }

    // spawns the picker panel between the press point and the viewer
    [SyncMethod]
    public void OnSwatchPressed(Button button, UIInteractionContext context)
    {
        var field = Field;
        if (field == null || field.IsDestroyed || World == null)
            return;
        var head = World.LocalUser?.Root?.HeadSlot;
        float3 point = context.WorldPoint;
        float3 offset = head != null && (head.GlobalPosition - point).LengthSquared > 1e-6f
            ? (head.GlobalPosition - point).Normalized * 0.3f
            : float3.Up * 0.1f;
        ColorPickerPanel.Spawn(World, field, MemberPath.Value ?? "", point + offset);
    }

    protected override void RefreshDisplay()
    {
        var value = GetMemberValue();
        color display = value switch
        {
            color c => c,
            colorHDR hdr => new color(hdr.r, hdr.g, hdr.b, hdr.a),
            _ => color.White
        };

        var swatch = _swatch.Target;
        if (swatch != null && !swatch.IsDestroyed)
            swatch.Tint.Value = new color(display.r, display.g, display.b, 1f); // left half: hue solid

        var alphaSwatch = _alphaSwatch.Target;
        if (alphaSwatch != null && !alphaSwatch.IsDestroyed)
            alphaSwatch.Tint.Value = display; // right half: true alpha over the checker
    }
}
