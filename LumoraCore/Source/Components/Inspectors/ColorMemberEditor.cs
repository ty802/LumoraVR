// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Globalization;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// R/G/B/A text fields plus a live swatch, for color and colorHDR members (their float fields are
/// addressed via the leaf paths r/g/b/a on top of this editor's own path).
/// </summary>
public class ColorMemberEditor : MemberEditor
{
    private readonly SyncRef<Image> _swatch;

    public ColorMemberEditor()
    {
        _swatch = new SyncRef<Image>(this);
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

        ui.PushStyle();
        ui.MinWidth(InspectorUI.RowHeight);
        ui.PreferredWidth(InspectorUI.RowHeight);
        ui.FlexibleWidth(0f);
        var swatchSlot = ui.Next("Swatch");
        _swatch.Target = swatchSlot.AttachComponent<Image>(); // image on the slot itself: sized by the row layout
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        var swatch = _swatch.Target;
        if (swatch == null || swatch.IsDestroyed)
            return;

        var value = GetMemberValue();
        color display = value switch
        {
            color c => c,
            colorHDR hdr => new color(hdr.r, hdr.g, hdr.b, hdr.a),
            _ => color.White
        };
        display.a = 1f; // the swatch shows the hue; alpha has its own field
        swatch.Tint.Value = display;
    }
}
