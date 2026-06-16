// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard exit screen: a centered card offering "Exit and Save" (commit settings, then quit)
/// or "Exit and Discard" (quit without persisting). Switching to another tab cancels. Settings
/// apply live for preview but are only written to disk here, so this is where saving happens.
/// </summary>
public sealed class ExitScreen : DashboardScreen
{
    private const float CornerRadius = 12f;

    private static readonly color CardFill = new color(0.14f, 0.13f, 0.21f, 0.96f);
    private static readonly color CardBorder = new color(0.52f, 0.46f, 0.82f, 0.50f);
    private static readonly color SaveFill = new color(0.24f, 0.56f, 0.38f, 0.95f);
    private static readonly color DiscardFill = new color(0.70f, 0.22f, 0.26f, 0.95f);
    private static readonly color TextPrimary = new color(0.95f, 0.95f, 0.98f, 1f);
    private static readonly color TextDim = new color(0.72f, 0.72f, 0.80f, 1f);
    private static readonly color ExitRed = new color(1f, 0.34f, 0.36f, 1f);

    public override color NavLabelColor => ExitRed;

    private Dashboard? _dashboard;
    private Text? _titleText;
    private Text? _messageText;
    private Slot? _buttonsSlot;
    private bool _exiting;

    protected override void BuildContent(UIBuilder builder)
    {
        _dashboard = Slot.GetComponentInParents<Dashboard>();

        var card = builder.Current.AddSlot("ExitCard");
        var cardRect = card.AttachComponent<RectTransform>();
        cardRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        cardRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        cardRect.OffsetMin.Value = new float2(-300f, -160f);
        cardRect.OffsetMax.Value = new float2(300f, 160f);
        ApplyRoundedPanel(card, CardFill, CardBorder);

        var col = card.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 18f;
        col.PaddingLeft.Value = 28f;
        col.PaddingRight.Value = 28f;
        col.PaddingTop.Value = 28f;
        col.PaddingBottom.Value = 28f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        _titleText = AddLabel(card, "Title", "Exit Lumora", 30f, TextPrimary, 46f);
        _messageText = AddLabel(card, "Message", "Save your changes, or exit and discard them.", 18f, TextDim, 30f);

        _buttonsSlot = card.AddSlot("Buttons");
        _buttonsSlot.AttachComponent<RectTransform>();
        SetFixedHeight(_buttonsSlot, 60f);
        var row = _buttonsSlot.AttachComponent<HorizontalLayout>();
        row.Spacing.Value = 14f;
        row.ForceExpandWidth.Value = true;
        row.ForceExpandHeight.Value = true;

        AddButton(_buttonsSlot, "Exit and Save", SaveFill, OnExitAndSave);
        AddButton(_buttonsSlot, "Exit and Discard", DiscardFill, OnExitAndDiscard);

        AddLabel(card, "Hint", "Pick another tab to cancel.", 14f, TextDim, 22f);
    }

    private void OnExitAndSave() => BeginExit(save: true);

    private void OnExitAndDiscard() => BeginExit(save: false);

    // Show a brief "saving / exiting" state and let it render, then do the (blocking) save and quit
    // a few updates later - so there's visible feedback instead of an instant close.
    private void BeginExit(bool save)
    {
        if (_exiting)
            return;
        _exiting = true;

        if (_titleText != null)
            _titleText.Content.Value = save ? "Saving and exiting…" : "Exiting…";
        if (_messageText != null)
            _messageText.Content.Value = "Please wait…";
        if (_buttonsSlot != null)
            _buttonsSlot.ActiveSelf.Value = false;
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();

        World.RunInUpdates(3, () =>
        {
            if (save)
            {
                EngineSettings.Commit();
                var home = Lumora.Core.Engine.Current?.WorldManager?.GetWorldByName("LocalHome");
                if (home != null)
                    WorldStorage.SaveToFile(home, Lumora.Core.Engine.LocalHomeSavePath);
            }
            Lumora.Core.Engine.Current?.RequestQuit();
        });
    }

    private Text AddLabel(Slot parent, string name, string content, float size, color textColor, float height)
    {
        var slot = parent.AddSlot(name);
        slot.AttachComponent<RectTransform>();
        SetFixedHeight(slot, height);
        var text = slot.AttachComponent<Text>();
        text.Content.Value = content;
        text.Font.Target = _dashboard?.Font.Target!;
        text.Size.Value = size;
        text.Color.Value = textColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    private void AddButton(Slot parent, string label, color fill, System.Action onClick)
    {
        var buttonSlot = parent.AddSlot(label);
        buttonSlot.AttachComponent<RectTransform>();
        var element = buttonSlot.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.FlexibleHeight.Value = 1f;

        ApplyRoundedPanel(buttonSlot, fill, CardBorder);

        var button = buttonSlot.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();

        var labelSlot = buttonSlot.AddSlot("Label");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;
        labelRect.OffsetMin.Value = float2.Zero;
        labelRect.OffsetMax.Value = float2.Zero;
        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = label;
        text.Font.Target = _dashboard?.Font.Target!;
        text.Size.Value = 18f;
        text.Color.Value = TextPrimary;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
    }

    private void ApplyRoundedPanel(Slot slot, color fill, color border)
    {
        var image = slot.AttachComponent<BorderedImage>();
        image.Tint.Value = fill;
        image.BorderTint.Value = border;
        var rounded = _dashboard?.RoundedSprite;
        if (rounded != null)
        {
            image.Texture.Target = rounded;
            image.NineSlice.Value = true;
            image.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }
    }

    private static void SetFixedHeight(Slot slot, float height)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
    }
}
