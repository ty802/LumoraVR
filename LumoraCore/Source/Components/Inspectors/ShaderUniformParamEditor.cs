// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;

namespace Lumora.Core.Components;

// Builds a type-appropriate editor for one shader uniform parameter: a ranged float/int gets a slider,
// a color vec4 gets channel fields plus a live swatch, a texture gets the drop-assignable texture
// editor, and other vectors get one numeric field per channel. Shared by the focused material panel and
// the generic collection editor so a ShaderUniformParam never falls back to a bare type-name label. -xlinka
public static class ShaderUniformParamEditor
{
    private static readonly string[] Leaves = { "x", "y", "z", "w" };

    // full inline row content for a param used as a generic list element: "name:" then the value
    // editor. the caller supplies the row and its horizontal layout.
    public static void BuildInlineEditor(ShaderUniformParam param, UIBuilder ui, Slot editorSlot)
    {
        string name = string.IsNullOrEmpty(param.Name.Value) ? "uniform" : param.Name.Value;

        ui.PushStyle();
        ui.MinWidth(120f);
        ui.PreferredWidth(150f);
        ui.FlexibleWidth(0f);
        var label = ui.Text($"{name}:", InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(label.RectTransform!);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var area = ui.Next("Value");
        var areaLayout = area.AttachComponent<HorizontalLayout>();
        areaLayout.Spacing.Value = 4f;
        areaLayout.ForceExpandHeight.Value = true;
        ui.NestInto(area);
        BuildValueEditor(area, ui, param);
        ui.NestOut();
        ui.PopStyle();
    }

    // just the type-appropriate value editor, built into an area that already has a horizontal
    // layout. reused by the focused material panel.
    public static void BuildValueEditor(Slot editorArea, UIBuilder ui, ShaderUniformParam param)
    {
        switch (param.Type.Value)
        {
            case ShaderUniformType.Texture2D:
                editorArea.AttachComponent<TextureRefMemberEditor>().Setup(param.Texture, "", ui);
                return;

            case ShaderUniformType.Float:
            case ShaderUniformType.Int:
                if (param.HasRange.Value)
                {
                    var slider = editorArea.AttachComponent<SliderMemberEditor>();
                    slider.Min.Value = param.Range.Value.x;
                    slider.Max.Value = param.Range.Value.y;
                    slider.WholeNumbers.Value = param.Type.Value == ShaderUniformType.Int;
                    slider.Setup(param.Value, "x", ui);
                }
                else
                {
                    editorArea.AttachComponent<PrimitiveMemberEditor>().Setup(param.Value, "x", ui);
                }
                return;

            case ShaderUniformType.Bool:
                // The value rides the x component (0/1); a plain field keeps the write honest.
                editorArea.AttachComponent<PrimitiveMemberEditor>().Setup(param.Value, "x", ui);
                return;

            case ShaderUniformType.Vec2:
                BuildComponentFields(ui, param, 2, withSwatch: false);
                return;
            case ShaderUniformType.Vec3:
                BuildComponentFields(ui, param, 3, withSwatch: false);
                return;
            case ShaderUniformType.Vec4:
                BuildComponentFields(ui, param, 4, withSwatch: param.IsColor.Value);
                return;
        }
    }

    // N numeric channel fields over the param's float4, plus a self-driving swatch for colors.
    private static void BuildComponentFields(UIBuilder ui, ShaderUniformParam param, int components, bool withSwatch)
    {
        for (int i = 0; i < components; i++)
        {
            ui.PushStyle();
            ui.FlexibleWidth(1f);
            var leafSlot = ui.Next(Leaves[i]);
            leafSlot.AttachComponent<HorizontalLayout>();
            ui.NestInto(leafSlot);
            leafSlot.AttachComponent<PrimitiveMemberEditor>().Setup(param.Value, Leaves[i], ui);
            ui.NestOut();
            ui.PopStyle();
        }

        if (!withSwatch)
            return;

        ui.PushStyle();
        ui.MinWidth(InspectorUI.RowHeight);
        ui.PreferredWidth(InspectorUI.RowHeight);
        ui.FlexibleWidth(0f);
        var swatchSlot = ui.Next("Swatch");
        var swatch = swatchSlot.AttachComponent<Image>();
        var initial = param.Value.Value;
        swatch.Tint.Value = new Lumora.Core.Math.color(initial.x, initial.y, initial.z, 1f);
        var driver = swatchSlot.AttachComponent<ShaderUniformSwatchDriver>();
        driver.Swatch.Target = swatch;
        driver.Param.Target = param;
        ui.PopStyle();
    }
}
