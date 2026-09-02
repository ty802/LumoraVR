// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// One modal at a time over the dashboard, with a scrim that both dims and eats every press behind it.
//
// Two mechanisms do the covering and they are not the same thing:
//   - the RENDER band. The scrim and the panel each get their own GraphicChunkRoot with an OverlayLevel,
//     which reserves a render-priority band above every normal chunk in the canvas. Without it the
//     dialog fights the nav bar for draw order and loses in places.
//   - the INPUT block. An InteractionBlock on the scrim. The canvas hit scan walks the tree in order and
//     the last match wins, so anything built BEFORE the scrim is overridden by the block and anything
//     built after (the panel) still gets its presses. That ordering is the whole exclusivity guarantee,
//     which is why the scrim slot is always added before the panel slot. -xlinka
//
// Requests stack. Opening a second dialog parks the first and rebuilds it when the second closes, so a
// confirm raised from inside a prompt does not silently eat the prompt.
[SingleInstancePerSlot]
[ComponentCategory("UI")]
public sealed class ModalHost : UIComponent
{
    private const float PanelWidth = 560f;
    private const float SidePadding = 24f;
    private const float RowSpacing = 16f;
    private const float TitleHeight = 40f;
    private const float MessageHeight = 72f;
    private const float PromptHeight = 40f;
    private const float ButtonsHeight = 52f;
    private const float CornerRadius = DashTheme.RadiusCard;

    private static readonly color ScrimFill = new color(0.02f, 0.02f, 0.05f, 0.62f);
    private static readonly color PanelFill = DashTheme.Panel;
    private static readonly color PanelBorder = DashTheme.OutlineStrong;
    private static readonly color NeutralFill = DashTheme.Surface;
    private static readonly color AccentFill = DashTheme.Accent;
    private static readonly color DestructiveFill = DashTheme.Negative;
    private static readonly color FieldFill = DashTheme.Field;
    private static readonly color TextPrimary = DashTheme.Text;
    private static readonly color TextDim = DashTheme.TextDim;

    private static readonly LocaleText CancelLabel = "Modal.Cancel".AsLocale("Cancel");
    private static readonly LocaleText ConfirmLabel = "Modal.Confirm".AsLocale("Confirm");
    private static readonly LocaleText OkLabel = "Modal.OK".AsLocale("OK");

    private readonly List<ModalRequest> _stack = new();
    private Slot? _scrim;
    private Slot? _panel;
    private ModalDialogRelay? _relay;
    private TextInput? _promptInput;
    private TextInput? _returnFocus;

    public bool IsOpen => _stack.Count > 0;
    public int Depth => _stack.Count;
    public ModalRequest? Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
    public Slot? ScrimSlot => _scrim;
    public Slot? PanelSlot => _panel;
    public TextInput? PromptInput => _promptInput;
    public IReadOnlyList<Button> OptionButtons => _relay?.Options ?? (IReadOnlyList<Button>)Array.Empty<Button>();

    // Raised after a dialog closes, with the slot that asked for it. A screen uses this to put its own
    // selection back.
    public event Action<ModalHost, Slot?>? Closed;

    // Nearest host above this slot, creating one on the dashboard (or failing that the canvas root) if
    // nothing has needed a modal here yet. Never returns null for a slot that is in a world, so callers
    // do not grow a "no modal available" branch that would quietly skip a confirmation.
    public static ModalHost? For(Slot? context)
    {
        if (context == null || context.IsDestroyed)
            return null;
        var existing = context.GetComponentInParents<ModalHost>();
        if (existing != null && !existing.IsDestroyed)
            return existing;

        var owner = context.GetComponentInParents<Dashboard>()?.Slot
            ?? context.GetComponentInParents<Canvas>()?.Slot
            ?? context;
        return owner.GetComponent<ModalHost>() ?? owner.AttachComponent<ModalHost>();
    }

    public static bool Confirm(Slot? context, LocaleText title, LocaleText message, LocaleText confirmLabel,
        Action confirmed, bool destructive = true)
    {
        var host = For(context);
        if (host == null)
            return false;
        var request = new ModalRequest
        {
            Title = title,
            Message = message,
            Invoker = context,
            CancelIndex = 0,
            Completed = result =>
            {
                if (result.Index == 1)
                    confirmed();
            },
        };
        request.AddOption(CancelLabel);
        request.AddOption(confirmLabel, destructive ? ModalOptionStyle.Destructive : ModalOptionStyle.Accent);
        return host.Open(request);
    }

    public static bool Ask(Slot? context, LocaleText title, LocaleText message, string initial,
        Action<string> submitted, LocaleText placeholder = default)
    {
        var host = For(context);
        if (host == null)
            return false;
        var request = new ModalRequest
        {
            Title = title,
            Message = message,
            Invoker = context,
            Prompt = true,
            PromptInitial = initial ?? string.Empty,
            PromptPlaceholder = placeholder,
            CancelIndex = 0,
            DefaultIndex = 1,
            Completed = result =>
            {
                if (result.Index == 1)
                    submitted(result.Text);
            },
        };
        request.AddOption(CancelLabel);
        request.AddOption(OkLabel, ModalOptionStyle.Accent);
        return host.Open(request);
    }

    public bool Open(ModalRequest? request)
    {
        if (request == null || IsDestroyed || Slot == null || Slot.IsDestroyed)
            return false;
        if (request.Options.Count == 0)
            request.AddOption(OkLabel, ModalOptionStyle.Accent);

        // Whoever was typing loses the keyboard for the duration, and gets it back on close. A field
        // that keeps focus under a modal swallows every key the dialog wanted.
        if (_stack.Count == 0)
        {
            _returnFocus = TextInput.Focused;
            _returnFocus?.Unfocus();
        }

        _stack.Add(request);
        Build(request);
        return true;
    }

    // A button was pressed. index is its position in the request's option list.
    public void Complete(int index)
    {
        if (_stack.Count == 0)
            return;
        var request = _stack[_stack.Count - 1];
        _stack.RemoveAt(_stack.Count - 1);
        string text = _relay?.PromptText ?? string.Empty;
        var invoker = request.Invoker;

        Teardown();
        if (_stack.Count > 0)
            Build(_stack[_stack.Count - 1]);
        else
            RestoreFocus();

        // The callback runs LAST so it is free to open another dialog on a host that is already idle.
        request.Completed?.Invoke(new ModalResult(index, text));
        Closed?.Invoke(this, invoker);
    }

    public void CompleteDefault()
    {
        var request = Current;
        Complete(request?.DefaultIndex ?? -1);
    }

    public void Dismiss()
    {
        var request = Current;
        if (request == null)
            return;
        if (!request.DismissOnScrim)
            return;
        Complete(request.CancelIndex);
    }

    // Everything goes away without any option being reported: a screen switch, the dashboard closing.
    // Each parked request still gets its callback so nothing is left waiting on an answer forever.
    public void CloseAll()
    {
        if (_stack.Count == 0)
            return;
        var pending = new List<ModalRequest>(_stack);
        _stack.Clear();
        Teardown();
        RestoreFocus();
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            pending[i].Completed?.Invoke(new ModalResult(pending[i].CancelIndex, string.Empty));
            Closed?.Invoke(this, pending[i].Invoker);
        }
    }

    public override void OnDestroy()
    {
        _stack.Clear();
        Teardown();
        base.OnDestroy();
    }

    private void RestoreFocus()
    {
        var target = _returnFocus;
        _returnFocus = null;
        if (target != null && !target.IsDestroyed && target.Slot != null && target.Slot.IsActive)
            target.Focus();
    }

    private void Teardown()
    {
        if (_promptInput != null && !_promptInput.IsDestroyed && _promptInput.IsFocused)
            _promptInput.Unfocus();
        _promptInput = null;
        _relay = null;
        if (_panel != null && !_panel.IsDestroyed)
            _panel.Destroy();
        _panel = null;
        if (_scrim != null && !_scrim.IsDestroyed)
            _scrim.Destroy();
        _scrim = null;
        MarkDirty();
    }

    // BUILD

    private void Build(ModalRequest request)
    {
        Teardown();

        var font = ResolveFont();
        var rounded = Slot.GetComponentInParents<Dashboard>()?.RoundedSprite;

        _scrim = Slot.AddSlot("ModalScrim");
        _scrim.Persistent.Value = false;
        var scrimRect = _scrim.AttachComponent<RectTransform>();
        Fill(scrimRect);
        _scrim.OrderOffset.Value = 20000L;
        _scrim.AttachComponent<GraphicChunkRoot>().OverlayLevel = 1;
        _scrim.AttachComponent<Image>().Tint.Value = ScrimFill;
        // Before the Button: the hit scan takes the last match on a slot, so the block goes first and
        // the button (when the dialog is dismissible) is allowed to win for the scrim itself.
        _scrim.AttachComponent<InteractionBlock>();

        _panel = Slot.AddSlot("ModalPanel");
        _panel.Persistent.Value = false;
        float height = MeasureHeight(request);
        var panelRect = _panel.AttachComponent<RectTransform>();
        panelRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        panelRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        panelRect.OffsetMin.Value = new float2(-PanelWidth * 0.5f, -height * 0.5f);
        panelRect.OffsetMax.Value = new float2(PanelWidth * 0.5f, height * 0.5f);
        _panel.OrderOffset.Value = 20001L;
        _panel.AttachComponent<GraphicChunkRoot>().OverlayLevel = 2;
        RoundedPanel(_panel, PanelFill, PanelBorder, rounded);
        // Absorbs presses on the panel background so they never fall through to the scrim's dismiss.
        _panel.AttachComponent<Button>();

        _relay = _panel.AttachComponent<ModalDialogRelay>();
        _relay.Host = this;

        if (request.DismissOnScrim)
            _scrim.AttachComponent<Button>().SetAction(_relay.OnScrimPressed);

        var column = _panel.AttachComponent<VerticalLayout>();
        column.Spacing.Value = RowSpacing;
        column.PaddingLeft.Value = SidePadding;
        column.PaddingRight.Value = SidePadding;
        column.PaddingTop.Value = SidePadding;
        column.PaddingBottom.Value = SidePadding;
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;

        AddLabel(_panel, "Title", request.Title, 26f, TextPrimary, TitleHeight, font);
        AddLabel(_panel, "Message", request.Message, 17f, TextDim, MessageHeight, font, wrap: true);

        if (request.Prompt)
            BuildPrompt(_panel, request, font);

        BuildButtons(_panel, request, font, rounded);

        MarkDirty();
    }

    private void BuildPrompt(Slot panel, ModalRequest request, IAssetProvider<FontSet>? font)
    {
        var host = panel.AddSlot("PromptRow");
        host.AttachComponent<RectTransform>();
        SetFixedHeight(host, PromptHeight);

        var builder = new UIBuilder(host);
        builder.Font(font)
            .TextColor(TextPrimary)
            .ForegroundColor(AccentFill)
            .BackgroundColor(FieldFill);
        var input = builder.TextInput(request.PromptInitial, request.PromptPlaceholder.Resolve());
        Fill(input.Slot.GetComponent<RectTransform>()!);
        input.SetSubmitAction(_relay!.OnPromptSubmitted);
        _relay.PromptInput = input;
        _promptInput = input;

        // The field owns the keyboard the moment the dialog appears - in VR that is also what puts the
        // on-screen keyboard in front of the user, and on desktop it saves a click nobody would guess
        // they had to make.
        input.Focus();
    }

    private void BuildButtons(Slot panel, ModalRequest request, IAssetProvider<FontSet>? font,
        IAssetProvider<TextureAsset>? rounded)
    {
        var row = panel.AddSlot("Buttons");
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, ButtonsHeight);
        var layout = row.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = 12f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = true;

        for (int i = 0; i < request.Options.Count; i++)
        {
            var option = request.Options[i];
            var cell = row.AddSlot("Option");
            cell.AttachComponent<RectTransform>();
            var element = cell.AttachComponent<LayoutElement>();
            element.FlexibleWidth.Value = 1f;
            element.FlexibleHeight.Value = 1f;
            RoundedPanel(cell, FillFor(option.Style), PanelBorder, rounded);

            var button = cell.AttachComponent<Button>();
            button.SetAction(_relay!.OnOptionPressed);
            _relay.Options.Add(button);

            AddFillLabel(cell, option.Label, 18f, TextPrimary, font);
        }
    }

    private static color FillFor(ModalOptionStyle style) => style switch
    {
        ModalOptionStyle.Accent => AccentFill,
        ModalOptionStyle.Destructive => DestructiveFill,
        _ => NeutralFill,
    };

    private static float MeasureHeight(ModalRequest request)
    {
        float height = SidePadding * 2f + TitleHeight + MessageHeight + ButtonsHeight + RowSpacing * 2f;
        if (request.Prompt)
            height += PromptHeight + RowSpacing;
        return height;
    }

    private IAssetProvider<FontSet>? ResolveFont()
    {
        var dashboard = Slot.GetComponentInParents<Dashboard>();
        if (dashboard?.Font.Target != null)
            return dashboard.Font.Target;
        // No dashboard above us (a world-space panel with its own canvas). Borrow whatever font the
        // canvas is already drawing with - text with no font renders NOTHING, so an empty dialog is the
        // failure mode this avoids.
        var canvasSlot = Slot.GetComponentInParents<Canvas>()?.Slot ?? Slot;
        foreach (var text in canvasSlot.GetComponentsInChildren<Text>())
        {
            if (text.Font.Target != null)
                return text.Font.Target;
        }
        return null;
    }

    private static Text AddLabel(Slot parent, string name, in LocaleText content, float size, color textColor,
        float height, IAssetProvider<FontSet>? font, bool wrap = false)
    {
        var slot = parent.AddSlot(name);
        slot.AttachComponent<RectTransform>();
        SetFixedHeight(slot, height);
        var text = slot.AttachComponent<Text>();
        text.Font.Target = font!;
        text.Size.Value = size;
        text.Color.Value = textColor;
        text.WordWrap.Value = wrap;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return LocaleTextRegistry.Bind(text, in content);
    }

    private static Text AddFillLabel(Slot parent, in LocaleText content, float size, color textColor,
        IAssetProvider<FontSet>? font)
    {
        var slot = parent.AddSlot("Label");
        Fill(slot.AttachComponent<RectTransform>());
        var text = slot.AttachComponent<Text>();
        text.Font.Target = font!;
        text.Size.Value = size;
        text.Color.Value = textColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return LocaleTextRegistry.Bind(text, in content);
    }

    private static void RoundedPanel(Slot slot, color fill, color border, IAssetProvider<TextureAsset>? rounded)
    {
        var image = slot.GetComponent<BorderedImage>() ?? slot.AttachComponent<BorderedImage>();
        image.Tint.Value = fill;
        image.BorderTint.Value = border;
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
        element.FlexibleWidth.Value = 1f;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private void MarkDirty() => Slot?.GetComponentInParents<Canvas>()?.MarkLayoutDirty();
}
