// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// dotted path addresses one leaf inside the field's value ("x", "r"; "" = the whole value).
// editors read/write the WHOLE boxed value through a StructMemberAccessor, so one non-generic
// component edits any value type.
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

    // also called on remote changes
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

    // Whether this editor must refuse writes to the EDITED MEMBER (not this editor component's own
    // drive state, which is what ComponentBase.IsDriven answers). A driven value is DERIVED: the
    // editor shows it live but must not author it, or the write lands for one frame and the driver
    // stamps over it - an edit box that silently rejects everything you type.
    // A hooked link is the passthrough escape: a hook INTERCEPTS the write and decides what to do
    // with it rather than rejecting it, so the editor stays live and lets the hook arbitrate. Same
    // rule as the datamodel's IsBlockedByDrive. -xlinka
    //
    // A member inside an ImmutableComponent subtree is read-only for the same reason it is not in the
    // tree: the person is not the one who authors it. Nothing else changes - the row still shows the
    // live value, and whatever composes it keeps writing.
    protected bool IsReadOnly
        => TargetMember.Target is ILinkable { IsDestroyed: false, IsDriven: true, IsHooked: false }
           || ImmutableComponent.IsProtected(TargetMember.Target);

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
        if (field == null || accessor == null || IsReadOnly)
            return;

        object? before = field.BoxedValue;
        object? after = accessor.SetValue(before, leaf);
        // BoxedValue is typed non-null, but a reference-type field's new value can legitimately be null. -xlinka
        field.BoxedValue = after!;
        InspectorUndo.RecordEdit(this, field, before, after);
    }

    // no undo record: live per-keystroke edits route here, editor records ONE edit for the
    // whole typing session on focus loss instead
    protected void SetMemberValueSilent(object? leaf)
    {
        var field = Field;
        var accessor = Accessor;
        if (field == null || accessor == null || IsReadOnly)
            return;
        object? after = accessor.SetValue(field.BoxedValue, leaf);
        field.BoxedValue = after!;
    }

    // shared tint rule: a member that has gone away reads broken, a driven one magenta,
    // a merely linked one cyan, anything else normal
    protected color FieldStateColor(color? normal = null)
        => InspectorUI.FieldStateColor(TargetMember.Target, normal);

    // Put a widget's color field on the row's live state tint so it re-colors as drives attach and
    // detach, and paint it once now. The tint component is attached to the ROW by the member-row
    // builder BEFORE the editor is built, so an editor always finds it in its parents; no tint
    // component (a standalone editor outside a member row) just means the build-time color stands.
    // Takes the color field rather than the graphic so any widget kind can be signalled. -xlinka
    protected void BindStateTint(IField<color>? widgetTint, color baseColor)
    {
        if (widgetTint == null || widgetTint.IsDestroyed)
            return;
        InspectorUI.ApplyStateTint(widgetTint, FieldStateColor(baseColor));
        var tint = Slot?.GetComponentInParent<MemberStateTint>();
        if (tint == null)
            return;
        tint.StateTint.Target = widgetTint;
        tint.TintBaseColor.Value = baseColor;
    }
}

public static class InspectorUI
{
    public static readonly color TextColor = new color(0.93f, 0.94f, 0.98f, 1f);
    public static readonly color MutedColor = new color(0.64f, 0.63f, 0.74f, 1f);
    // slot's own persistence flag is off
    public static readonly color NonPersistentColor = new color(0.95f, 0.58f, 0.20f, 1f);
    // non-persistent only through an ancestor
    public static readonly color NonPersistentInheritedColor = new color(0.78f, 0.56f, 0.34f, 1f);
    public static readonly color DrivenColor = new color(0.85f, 0.45f, 0.9f, 1f);
    // Linked but not driving (a hook/link holds the field without writing it) - reads cyan.
    public static readonly color LinkedColor = new color(0.38f, 0.78f, 0.85f, 1f);
    // a member the editor can no longer resolve (destroyed or never wired)
    public static readonly color BrokenColor = new color(0.50f, 0.50f, 0.52f, 1f);
    // Section headers/dividers in inspector panels.
    public static readonly color CyanColor = new color(0.35f, 0.75f, 0.90f, 1f);
    public static readonly color RowColor = new color(0.10f, 0.09f, 0.16f, 1f);
    public static readonly color HeaderColor = new color(0.15f, 0.13f, 0.22f, 1f);
    // opaque near-black backing for the hierarchy and detail panes
    public static readonly color PaneColor = new color(0.09f, 0.08f, 0.13f, 1f);
    public static readonly color AccentColor = new color(0.45f, 0.38f, 0.80f, 1f);
    // row backing for the selected hierarchy entry
    public static readonly color SelectionColor = new color(0.45f, 0.38f, 0.80f, 0.60f);
    public static readonly color DangerColor = new color(0.70f, 0.24f, 0.28f, 1f);
    public static readonly color AxisXColor = new color(0.90f, 0.30f, 0.32f, 1f);
    public static readonly color AxisYColor = new color(0.36f, 0.80f, 0.42f, 1f);
    public static readonly color AxisZColor = new color(0.35f, 0.55f, 0.95f, 1f);
    public const float RowHeight = 30f;
    public const float FontSize = 15f;

    // THE field-state color rule, one implementation for every editor and every widget in a member
    // row: a member that is gone reads broken gray, a driven one the drive magenta, one that is
    // merely linked (something holds it but nothing writes it) cyan, anything else the caller's
    // normal color. Keep new editors on this instead of hand-rolling a tint, or the row's chip, its
    // label and its value widget end up disagreeing about what state the field is in. -xlinka
    public static color FieldStateColor(IWorldElement? member, color? normal = null)
    {
        // Broken means the MEMBER ITSELF is gone. A member kind that simply has no link machinery
        // (a delegate, a bag) is normal, not broken.
        if (member == null || member.IsDestroyed)
            return BrokenColor;
        if (member is not ILinkable linkable)
            return normal ?? TextColor;
        if (linkable.IsDriven)
            return DrivenColor;
        if (linkable.IsLinked)
            return LinkedColor;
        return normal ?? TextColor;
    }

    // Paint a widget's backing with a field-state color.
    // Helio buttons DRIVE their own Image.Tint from an interaction ColorDriver (normal/hover/pressed),
    // so writing the tint directly is silently reverted on the driver's next pass - the base color has
    // to be retuned on the DRIVER instead, which also re-derives the hover and pressed shades from it.
    // A plain image (a text input backing) has no driver and takes the write. Anything tinting an
    // inspector widget must go through here or it will look like it worked and then flicker back on
    // the first hover. -xlinka
    public static void ApplyStateTint(IField<color>? widgetTint, in color value)
    {
        if (widgetTint == null || widgetTint.IsDestroyed)
            return;

        // The color field's parent is the graphic component, whose slot carries any interaction
        // ColorDriver bound to it. Every Helio Button installs one on its own backing in OnAttach.
        var slot = ((widgetTint as SyncElement)?.Parent as Component)?.Slot;
        if (slot != null)
        {
            foreach (var driver in slot.GetComponents<ColorDriver>())
            {
                if (!ReferenceEquals(driver.Target.Target, widgetTint))
                    continue;
                driver.SetColors(value);
                return;
            }
        }
        widgetTint.Value = value;
    }

    // text without a font renders nothing - the first inspector shipped with invisible content this way
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

    // Returns the header ROW so a caller can hang a control off the right-hand end of it - the label
    // takes the flexible width, so anything appended after lands hard right. -xlinka
    public static Slot SectionHeader(Slot parent, string label, Slot themeContext)
    {
        var row = FixedRow(parent, "Section", 26f, out var ui, themeContext);
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var text = ui.Text(label, FontSize - 1f, CyanColor);
        FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Bottom;
        ui.PopStyle();

        var divider = parent.AddSlot("Divider");
        divider.AttachComponent<RectTransform>();
        var dividerLE = divider.AttachComponent<Helio.UI.Layout.LayoutElement>();
        dividerLE.MinHeight.Value = 4f;
        dividerLE.PreferredHeight.Value = 4f;
        var rule = divider.AttachComponent<Image>();
        rule.Tint.Value = new color(CyanColor.r, CyanColor.g, CyanColor.b, 0.85f);
        return row;
    }

    // a bare RectTransform defaults to a 100x100 centered chunk
    public static void FillParent(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

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

    // color coding: purple ref, blue field, green list, magenta driven. pressing it pops the
    // member's reference card out of the panel when a pull source is wired; otherwise just the marker
    public static void MemberChip(UIBuilder ui, color tint, ReferenceProxySource? pullSource)
    {
        ui.PushStyle();
        ui.MinWidth(14f);
        ui.PreferredWidth(14f);
        ui.FlexibleWidth(0f);
        if (pullSource != null)
        {
            ui.PushStyle();
            ui.BackgroundColor(tint);
            var button = ui.Button("", null!);
            button.SetAction(pullSource.OnPullPressed);
            ui.PopStyle();
        }
        else
        {
            var chip = ui.Image(null, tint);
            FillParent(chip.RectTransform!);
        }
        ui.PopStyle();
    }

    // synced argument routed to an IInspectorActionHandler, never closures
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
