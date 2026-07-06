// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Base of every inspector field editor: holds the edited sync field plus a dotted path addressing
/// one leaf inside its value ("x", "r"; "" = the whole value). Editors read/write the WHOLE boxed
/// value through a StructMemberAccessor, so one non-generic component edits any value type.
/// </summary>
public abstract class MemberEditor : Component
{
    public readonly SyncRef<IWorldElement> TargetMember;
    public readonly Sync<string> MemberPath;

    private IChangeable? _watched;

    public MemberEditor()
    {
        TargetMember = new SyncRef<IWorldElement>(this);
        MemberPath = new Sync<string>(this, "");
    }

    protected IField? Field => TargetMember.Target as IField;

    protected StructMemberAccessor? Accessor
    {
        get
        {
            var field = Field;
            return field == null ? null : StructMemberAccessor.Get(field.ValueType, MemberPath.Value ?? "");
        }
    }

    protected Type? LeafType => Accessor?.LeafType;

    public void Setup(IField field, string path, UIBuilder ui)
    {
        TargetMember.Target = field;
        MemberPath.Value = path ?? "";
        BuildUI(ui);
        WatchField();
        RefreshDisplay();
    }

    protected abstract void BuildUI(UIBuilder ui);

    /// <summary>Pull the current member value into the widgets (also called on remote changes).</summary>
    protected virtual void RefreshDisplay() { }

    public override void OnStart()
    {
        base.OnStart();
        WatchField();
        RefreshDisplay();
    }

    public override void OnDestroy()
    {
        UnwatchField();
        base.OnDestroy();
    }

    private void WatchField()
    {
        UnwatchField();
        if (TargetMember.Target is IChangeable changeable)
        {
            _watched = changeable;
            changeable.Changed += OnFieldChanged;
        }
    }

    private void UnwatchField()
    {
        if (_watched != null)
        {
            _watched.Changed -= OnFieldChanged;
            _watched = null;
        }
    }

    private void OnFieldChanged(IChangeable element)
    {
        if (!IsDestroyed)
            RefreshDisplay();
    }

    protected object? GetMemberValue()
    {
        var field = Field;
        if (field == null)
            return null;
        try { return Accessor?.GetValue(field.BoxedValue); }
        catch { return null; }
    }

    protected void SetMemberValue(object? leaf)
    {
        var field = Field;
        var accessor = Accessor;
        if (field == null || accessor == null)
            return;

        object? before = field.BoxedValue;
        object? after = accessor.SetValue(before, leaf);
        // BoxedValue is typed non-null, but a reference-type field's new value can legitimately be null. -xlinka
        field.BoxedValue = after!;
        InspectorUndo.RecordEdit(this, field, before, after);
    }

    /// <summary>Tint hint for the editor's text (drive detection is per-editor where it matters).</summary>
    protected color FieldStateColor() => InspectorUI.TextColor;
}

/// <summary>Shared inspector UI construction helpers + palette.</summary>
public static class InspectorUI
{
    public static readonly color TextColor = new color(0.92f, 0.94f, 0.97f, 1f);
    public static readonly color MutedColor = new color(0.62f, 0.66f, 0.72f, 1f);
    public static readonly color DrivenColor = new color(0.85f, 0.45f, 0.9f, 1f);
    public static readonly color RowColor = new color(0.13f, 0.15f, 0.20f, 0.85f);
    public static readonly color HeaderColor = new color(0.16f, 0.19f, 0.26f, 0.95f);
    public static readonly color AccentColor = new color(0.35f, 0.6f, 0.95f, 1f);
    public static readonly color DangerColor = new color(0.85f, 0.3f, 0.3f, 1f);
    public const float RowHeight = 30f;
    public const float FontSize = 15f;

    /// <summary>
    /// Pull the surrounding UITheme into a fresh builder. Font above all: Helio text WITHOUT a font
    /// renders nothing, which is exactly how the first inspector shipped with invisible content.
    /// </summary>
    public static Lumora.Core.Components.UI.UITheme? ApplyTheme(UIBuilder ui, Slot context)
    {
        var theme = context.GetComponentInParent<Lumora.Core.Components.UI.UITheme>();
        if (theme != null)
        {
            if (theme.ThemeFont != null)
                ui.Font(theme.ThemeFont);
            ui.TextColor(theme.TextPrimary.Value);
            ui.BackgroundColor(theme.ButtonFill.Value);
            ui.RoundedSprite(theme.RoundedSprite);
        }
        return theme;
    }

    /// <summary>Background image + TextInput + child "Text" - the standard editable field.</summary>
    public static TextInput CreateTextInput(UIBuilder ui, string name = "Input")
    {
        var slot = ui.Next(name);
        ui.NestInto(slot);
        var image = slot.AttachComponent<Image>();
        image.Tint.Value = RowColor;
        var input = slot.AttachComponent<TextInput>();

        var textSlot = slot.AddSlot("Text");
        FillParent(textSlot.AttachComponent<RectTransform>());
        var text = textSlot.AttachComponent<Text>();
        text.Size.Value = FontSize;
        text.Color.Value = TextColor;
        var theme = slot.GetComponentInParent<Lumora.Core.Components.UI.UITheme>();
        if (theme?.ThemeFont != null)
            text.Font.Target = theme.ThemeFont;

        ui.NestOut();
        return input;
    }

    /// <summary>Anchor a rect to fill its parent (a bare RectTransform is a 100x100 centered chunk).</summary>
    public static void FillParent(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    /// <summary>
    /// The proven fixed-height row: slot + rect + LayoutElement(Min/Preferred) + HorizontalLayout,
    /// returning a themed builder rooted at the row.
    /// </summary>
    public static Slot FixedRow(Slot parent, string name, float height, out UIBuilder rowUi, Slot themeContext)
    {
        var row = parent.AddSlot(name);
        row.AttachComponent<RectTransform>();
        var le = row.AttachComponent<Helio.UI.Layout.LayoutElement>();
        le.MinHeight.Value = height;
        le.PreferredHeight.Value = height;
        var layout = row.AttachComponent<Helio.UI.Layout.HorizontalLayout>();
        layout.Spacing.Value = 6f;
        layout.PaddingLeft.Value = 4f;
        layout.PaddingRight.Value = 4f;
        layout.ForceExpandHeight.Value = true;
        layout.ForceExpandWidth.Value = false;
        // Own graphics chunk per row: a hover tint or text change re-tessellates ONE ROW's mesh
        // instead of the whole panel - the difference between smooth and slideshow on busy panels.
        row.AttachComponent<GraphicChunkRoot>();
        rowUi = new UIBuilder(row);
        ApplyTheme(rowUi, themeContext);
        return row;
    }

    /// <summary>Relay button: synced argument routed to an IInspectorActionHandler (never closures).</summary>
    public static Button RelayButton(UIBuilder ui, Component handler, string argument, string label, float width)
    {
        ui.PushStyle();
        if (width > 0f)
        {
            ui.MinWidth(width);
            ui.PreferredWidth(width);
            ui.FlexibleWidth(0f);
        }
        else
        {
            ui.FlexibleWidth(1f);
        }
        var button = ui.Button(label, null!);
        var relay = button.Slot.AttachComponent<InspectorButtonRelay>();
        relay.Argument.Value = argument;
        relay.Handler.Target = handler;
        button.SetAction(relay.OnPressed);
        var text = button.Slot.GetComponentInChildren<Text>();
        if (text != null)
            FillParent(text.RectTransform!);
        ui.PopStyle();
        return button;
    }
}
