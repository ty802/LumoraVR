// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Helio.UI;

// fluent UI construction. Nest()/NestOut() walk into/out of subtrees,
// Next() makes the next element a sibling under the current root. - xlinka
public class UIBuilder
{
    private readonly Stack<Slot> _roots = new();
    private readonly List<UIStyle> _styles = new();
    private int _styleIndex;

    public UIBuilder(Slot root, Slot? forceNext = null)
    {
        _roots.Push(root);
        Current = root;
        _styles.Add(UIStyle.Default);
        ForceNext = forceNext?.GetComponent<RectTransform>() ?? forceNext?.AttachComponent<RectTransform>();
    }

    public Slot Root => _roots.Peek();
    public Slot Current { get; private set; }
    public RectTransform? ForceNext { get; set; }

    public UIStyle Style => CurrentStyle;

    private UIStyle CurrentStyle
    {
        get => _styles[_styleIndex];
        set => _styles[_styleIndex] = value;
    }

    // create a sibling slot under the current root and make it current. - xlinka
    public Slot Next(string name = "")
    {
        if (ForceNext != null)
        {
            Current = ForceNext.Slot;
            ForceNext = null;
        }
        else
        {
            Current = Root.AddSlot(name);
            Current.AttachComponent<RectTransform>();
        }
        SetupCurrentLayoutElement();
        return Current;
    }

    // push Current as the new root. subsequent Next() calls go under it. - xlinka
    public void Nest()
    {
        _roots.Push(Current);
    }

    public void NestInto(Slot slot)
    {
        NestInto(slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>());
    }

    public void NestInto(RectTransform rect)
    {
        _roots.Push(rect.Slot);
        Current = rect.Slot;
    }

    public void NestOut()
    {
        if (_roots.Count <= 1) return;
        _roots.Pop();
        Current = _roots.Peek();
    }

    public UIBuilder PushStyle()
    {
        if (_styleIndex < _styles.Count - 1)
        {
            _styles.RemoveRange(_styleIndex + 1, _styles.Count - _styleIndex - 1);
        }

        _styles.Add(CurrentStyle);
        _styleIndex++;
        return this;
    }

    public UIBuilder PopStyle()
    {
        if (_styleIndex > 0)
        {
            _styles.RemoveAt(_styleIndex);
            _styleIndex--;
        }

        return this;
    }

    public UIBuilder SetStyle(UIStyle style)
    {
        CurrentStyle = style;
        return this;
    }

    public UIBuilder TextColor(color value)
    {
        var style = CurrentStyle;
        style.TextColor = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder ForegroundColor(color value)
    {
        var style = CurrentStyle;
        style.ForegroundColor = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder BackgroundColor(color value)
    {
        var style = CurrentStyle;
        style.BackgroundColor = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder FontSize(float value)
    {
        var style = CurrentStyle;
        style.FontSize = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder Font(IAssetProvider<FontSet>? value)
    {
        var style = CurrentStyle;
        style.Font = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder MinWidth(float value)
    {
        var style = CurrentStyle;
        style.MinWidth = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder MinHeight(float value)
    {
        var style = CurrentStyle;
        style.MinHeight = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder PreferredWidth(float value)
    {
        var style = CurrentStyle;
        style.PreferredWidth = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder PreferredHeight(float value)
    {
        var style = CurrentStyle;
        style.PreferredHeight = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder FlexibleWidth(float value)
    {
        var style = CurrentStyle;
        style.FlexibleWidth = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder RoundedSprite(IAssetProvider<TextureAsset>? value)
    {
        var style = CurrentStyle;
        style.RoundedSprite = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder UseZeroMetrics(bool value)
    {
        var style = CurrentStyle;
        style.UseZeroMetrics = value;
        CurrentStyle = style;
        return this;
    }

    public UIBuilder FlexibleHeight(float value)
    {
        var style = CurrentStyle;
        style.FlexibleHeight = value;
        CurrentStyle = style;
        return this;
    }

    public Slot Empty(string name = "Slot")
    {
        return Next(name);
    }

    public List<RectTransform> SplitHorizontally(params float[] proportions)
    {
        Normalize(proportions);
        var result = new List<RectTransform>(proportions.Length);
        float x = 0f;
        for (int i = 0; i < proportions.Length; i++)
        {
            float nextX = x + proportions[i];
            var rect = Empty("Split").GetComponent<RectTransform>();
            rect.AnchorMin.Value = new float2(x, 0f);
            rect.AnchorMax.Value = new float2(nextX, 1f);
            rect.OffsetMin.Value = float2.Zero;
            rect.OffsetMax.Value = float2.Zero;
            result.Add(rect);
            x = nextX;
        }
        return result;
    }

    public List<RectTransform> SplitVertically(params float[] proportions)
    {
        Normalize(proportions);
        var result = new List<RectTransform>(proportions.Length);
        float y = 1f;
        for (int i = 0; i < proportions.Length; i++)
        {
            float nextY = y - proportions[i];
            var rect = Empty("Split").GetComponent<RectTransform>();
            rect.AnchorMin.Value = new float2(0f, nextY);
            rect.AnchorMax.Value = new float2(1f, y);
            rect.OffsetMin.Value = float2.Zero;
            rect.OffsetMax.Value = float2.Zero;
            result.Add(rect);
            y = nextY;
        }
        return result;
    }

    public void SplitHorizontally(float leftProportion, out RectTransform left, out RectTransform right, float gap = 0f)
    {
        float halfGap = gap * 0.5f;
        left = Empty("Left").GetComponent<RectTransform>();
        right = Empty("Right").GetComponent<RectTransform>();
        left.AnchorMin.Value = new float2(0f, 0f);
        left.AnchorMax.Value = new float2(leftProportion - halfGap, 1f);
        right.AnchorMin.Value = new float2(leftProportion + halfGap, 0f);
        right.AnchorMax.Value = new float2(1f, 1f);
        left.OffsetMin.Value = left.OffsetMax.Value = float2.Zero;
        right.OffsetMin.Value = right.OffsetMax.Value = float2.Zero;
    }

    public void SplitVertically(float topProportion, out RectTransform top, out RectTransform bottom, float gap = 0f)
    {
        float bottomStart = 1f - topProportion;
        float halfGap = gap * 0.5f;
        top = Empty("Top").GetComponent<RectTransform>();
        bottom = Empty("Bottom").GetComponent<RectTransform>();
        top.AnchorMin.Value = new float2(0f, bottomStart + halfGap);
        top.AnchorMax.Value = new float2(1f, 1f);
        bottom.AnchorMin.Value = new float2(0f, 0f);
        bottom.AnchorMax.Value = new float2(1f, bottomStart - halfGap);
        top.OffsetMin.Value = top.OffsetMax.Value = float2.Zero;
        bottom.OffsetMin.Value = bottom.OffsetMax.Value = float2.Zero;
    }

    public void HorizontalHeader(float size, out RectTransform header, out RectTransform content)
    {
        header = Empty("Header").GetComponent<RectTransform>();
        content = Empty("Content").GetComponent<RectTransform>();
        header.AnchorMin.Value = new float2(0f, 1f);
        header.AnchorMax.Value = new float2(1f, 1f);
        header.OffsetMin.Value = new float2(0f, -size);
        header.OffsetMax.Value = float2.Zero;
        content.AnchorMin.Value = new float2(0f, 0f);
        content.AnchorMax.Value = new float2(1f, 1f);
        content.OffsetMin.Value = float2.Zero;
        content.OffsetMax.Value = new float2(0f, -size);
    }

    public void HorizontalFooter(float size, out RectTransform footer, out RectTransform content)
    {
        content = Empty("Content").GetComponent<RectTransform>();
        footer = Empty("Footer").GetComponent<RectTransform>();
        footer.AnchorMin.Value = new float2(0f, 0f);
        footer.AnchorMax.Value = new float2(1f, 0f);
        footer.OffsetMin.Value = float2.Zero;
        footer.OffsetMax.Value = new float2(0f, size);
        content.AnchorMin.Value = new float2(0f, 0f);
        content.AnchorMax.Value = new float2(1f, 1f);
        content.OffsetMin.Value = new float2(0f, size);
        content.OffsetMax.Value = float2.Zero;
    }

    public void VerticalHeader(float size, out RectTransform header, out RectTransform content)
    {
        header = Empty("Header").GetComponent<RectTransform>();
        content = Empty("Content").GetComponent<RectTransform>();
        header.AnchorMin.Value = new float2(0f, 0f);
        header.AnchorMax.Value = new float2(0f, 1f);
        header.OffsetMin.Value = float2.Zero;
        header.OffsetMax.Value = new float2(size, 0f);
        content.AnchorMin.Value = new float2(0f, 0f);
        content.AnchorMax.Value = new float2(1f, 1f);
        content.OffsetMin.Value = new float2(size, 0f);
        content.OffsetMax.Value = float2.Zero;
    }

    public void VerticalFooter(float size, out RectTransform footer, out RectTransform content)
    {
        content = Empty("Content").GetComponent<RectTransform>();
        footer = Empty("Footer").GetComponent<RectTransform>();
        footer.AnchorMin.Value = new float2(1f, 0f);
        footer.AnchorMax.Value = new float2(1f, 1f);
        footer.OffsetMin.Value = new float2(-size, 0f);
        footer.OffsetMax.Value = float2.Zero;
        content.AnchorMin.Value = new float2(0f, 0f);
        content.AnchorMax.Value = new float2(1f, 1f);
        content.OffsetMin.Value = float2.Zero;
        content.OffsetMax.Value = new float2(-size, 0f);
    }

    // layout containers

    public HorizontalLayout HorizontalLayout(float spacing = 0f, float padding = 0f)
    {
        Next("HorizontalLayout");
        var layout = Current.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = spacing;
        SetPadding(layout, padding);
        Nest();
        return layout;
    }

    public VerticalLayout VerticalLayout(float spacing = 0f, float padding = 0f)
    {
        Next("VerticalLayout");
        var layout = Current.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = spacing;
        SetPadding(layout, padding);
        Nest();
        return layout;
    }

    public GridLayout GridLayout(int columns = 3, float spacing = 0f, float padding = 0f)
    {
        Next("GridLayout");
        var layout = Current.AttachComponent<GridLayout>();
        layout.Columns.Value = columns;
        layout.Spacing.Value = spacing;
        SetPadding(layout, padding);
        Nest();
        return layout;
    }

    // Make the CURRENT slot shrink-wrap to its content (e.g. a VerticalLayout panel grows to fit its
    // rows). Attach AFTER the layout component on the same slot. Both axes fit to preferred by default. -xlinka
    public ContentSizeFitter FitContent()
        => FitContent(SizeFit.PreferredSize, SizeFit.PreferredSize);

    public ContentSizeFitter FitContent(SizeFit horizontal, SizeFit vertical)
    {
        var fitter = Current.AttachComponent<ContentSizeFitter>();
        fitter.HorizontalFit.Value = horizontal;
        fitter.VerticalFit.Value = vertical;
        return fitter;
    }

    // leaf elements

    public Image Image(IAssetProvider<TextureAsset>? texture = null, color? tint = null)
    {
        Next("Image");
        var image = Current.AttachComponent<Image>();
        if (texture != null) image.Texture.Target = texture;
        image.Tint.Value = tint ?? CurrentStyle.ForegroundColor;
        return image;
    }

    public RawImage RawImage(IAssetProvider<TextureAsset>? texture = null, color? tint = null, Rect? uvRect = null, bool preserveAspect = false)
    {
        Next("RawImage");
        var image = Current.AttachComponent<RawImage>();
        if (texture != null) image.Texture.Target = texture;
        image.Tint.Value = tint ?? color.White;
        image.UVRect.Value = uvRect ?? Rect.UnitRect;
        image.PreserveAspect.Value = preserveAspect;
        return image;
    }

    public TiledRawImage TiledRawImage(
        IAssetProvider<TextureAsset>? texture = null,
        color? tint = null,
        float2? tileSize = null,
        float2? tileOffset = null,
        global::Helio.UI.TiledRawImage.TileSizeBasis sizeBasis = global::Helio.UI.TiledRawImage.TileSizeBasis.Absolute)
    {
        Next("TiledRawImage");
        var image = Current.AttachComponent<TiledRawImage>();
        if (texture != null) image.Texture.Target = texture;
        image.Tint.Value = tint ?? color.White;
        image.TileSize.Value = tileSize ?? new float2(32f, 32f);
        image.TileOffset.Value = tileOffset ?? float2.Zero;
        image.SizeBasis.Value = sizeBasis;
        return image;
    }

    public Image Panel(color? background = null)
    {
        Next("Panel");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        return image;
    }

    public Mask Mask(color? background = null, bool showMaskGraphic = false)
    {
        Next("Mask");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        var mask = Current.AttachComponent<Mask>();
        mask.ShowMaskGraphic.Value = showMaskGraphic;
        return mask;
    }

    public Text Text(string content, float? size = null, color? color = null)
    {
        Next("Text");
        var text = Current.AttachComponent<Text>();
        text.Content.Value = content;
        text.Size.Value = size ?? CurrentStyle.FontSize;
        text.Color.Value = color ?? CurrentStyle.TextColor;
        text.Font.Target = CurrentStyle.Font!;
        return text;
    }

    // Translatable text. A raw string still binds the string overload above (identity conversion beats
    // the implicit one), so no existing call site changes behaviour; a keyed value registers itself and
    // re-resolves on a language switch.
    public Text Text(in LocaleText content, float? size = null, color? color = null)
        => LocaleTextRegistry.Bind(Text(content.Resolve(), size, color), in content);

    public Button Button(string label, Action<Button, UIInteractionContext>? clicked = null, color? background = null)
    {
        Next("Button");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        var button = Current.AttachComponent<Button>();
        button.SetAction(clicked);

        Nest();
        var text = Text(label, null, CurrentStyle.TextColor);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        Fill(text.RectTransform!);
        NestOut();
        return button;
    }

    public Button Button(in LocaleText label, Action<Button, UIInteractionContext>? clicked = null, color? background = null)
    {
        var button = Button(label.Resolve(), clicked, background);
        LocaleTextRegistry.Bind(button.Slot.FindChild("Text", recursive: false)?.GetComponent<Text>(), in label);
        return button;
    }

    public Checkbox Checkbox(bool isChecked = false, Action<Checkbox, bool>? changed = null, color? background = null)
    {
        Next("Checkbox");
        var checkbox = Current.AttachComponent<Checkbox>();
        checkbox.IsChecked.Value = isChecked;
        SetElementSize(Current, 24f, 24f);

        var boxSlot = Current.AddSlot("Box");
        var boxRect = boxSlot.AttachComponent<RectTransform>();
        boxRect.AnchorMin.Value = new float2(0f, 0.5f);
        boxRect.AnchorMax.Value = new float2(0f, 0.5f);
        boxRect.OffsetMin.Value = new float2(0f, -12f);
        boxRect.OffsetMax.Value = new float2(24f, 12f);
        var boxColor = background ?? CurrentStyle.BackgroundColor;
        IField<color> boxTint;
        if (CurrentStyle.RoundedSprite != null)
        {
            var boxImage = boxSlot.AttachComponent<BorderedImage>();
            boxImage.Tint.Value = boxColor;
            boxImage.BorderTint.Value = color.Transparent;
            boxImage.Texture.Target = CurrentStyle.RoundedSprite;
            boxImage.NineSlice.Value = true;
            boxImage.Borders.Value = new float4(6f, 6f, 6f, 6f);
            boxTint = boxImage.Tint;
        }
        else
        {
            var boxImage = boxSlot.AttachComponent<Image>();
            boxImage.Tint.Value = boxColor;
            boxTint = boxImage.Tint;
        }
        checkbox.AddColorDriver(boxTint, boxColor);

        var checkSlot = boxSlot.AddSlot("Check");
        var checkRect = checkSlot.AttachComponent<RectTransform>();
        checkRect.AnchorMin.Value = float2.Zero;
        checkRect.AnchorMax.Value = float2.One;
        checkRect.OffsetMin.Value = float2.Zero;
        checkRect.OffsetMax.Value = float2.Zero;
        var checkText = checkSlot.AttachComponent<Text>();
        checkText.Content.Value = "✓";
        checkText.Font.Target = CurrentStyle.Font!;
        checkText.Size.Value = 20f;
        checkText.Color.Value = CurrentStyle.ForegroundColor;
        checkText.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        checkText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        checkSlot.ActiveSelf.Value = isChecked;
        checkbox.SetCheckVisual(checkSlot.ActiveSelf);

        checkbox.SetAction(changed);

        return checkbox;
    }

    public Radio Radio(string group, bool isChecked = false, Action<Radio, bool>? changed = null, color? background = null)
    {
        Next("Radio");
        var radio = Current.AttachComponent<Radio>();
        radio.Group.Value = group;
        radio.IsChecked.Value = isChecked;
        SetElementSize(Current, 24f, 24f);

        var ringSlot = Current.AddSlot("Ring");
        var ringRect = ringSlot.AttachComponent<RectTransform>();
        ringRect.AnchorMin.Value = new float2(0f, 0.5f);
        ringRect.AnchorMax.Value = new float2(0f, 0.5f);
        ringRect.OffsetMin.Value = new float2(0f, -12f);
        ringRect.OffsetMax.Value = new float2(24f, 12f);
        var ringImage = ringSlot.AttachComponent<Image>();
        ringImage.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        radio.AddColorDriver(ringImage.Tint, ringImage.Tint.Value);

        var dotSlot = ringSlot.AddSlot("Dot");
        var dotRect = dotSlot.AttachComponent<RectTransform>();
        dotRect.AnchorMin.Value = new float2(0.3f, 0.3f);
        dotRect.AnchorMax.Value = new float2(0.7f, 0.7f);
        dotRect.OffsetMin.Value = float2.Zero;
        dotRect.OffsetMax.Value = float2.Zero;
        var dotImage = dotSlot.AttachComponent<Image>();
        dotImage.Tint.Value = CurrentStyle.ForegroundColor;
        dotSlot.ActiveSelf.Value = isChecked;
        radio.SetCheckVisual(dotSlot.ActiveSelf);

        if (changed != null)
        {
            radio.ValueChanged += changed;
        }

        return radio;
    }

    public Slider Slider(float value = 0f, float min = 0f, float max = 1f, Action<Slider, float>? changed = null, color? background = null)
    {
        Next("Slider");
        var slider = Current.AttachComponent<Slider>();
        slider.Min.Value = min;
        slider.Max.Value = max;
        slider.Value.Value = value;
        SetElementSize(Current, 96f, 24f);

        float initialT = max > min ? (value - min) / (max - min) : 0f;
        const float handleRadius = 9f;

        // One solid full-width track bar - a clear, defined line (no fill, no
        // soft nine-slice fade). Inset by the handle radius so the handle's
        // travel range and the track ends line up.
        var trackSlot = Current.AddSlot("Track");
        var trackRect = trackSlot.AttachComponent<RectTransform>();
        trackRect.AnchorMin.Value = new float2(0f, 0.5f);
        trackRect.AnchorMax.Value = new float2(1f, 0.5f);
        trackRect.OffsetMin.Value = new float2(handleRadius, -3f);
        trackRect.OffsetMax.Value = new float2(-handleRadius, 3f);
        var trackImage = trackSlot.AttachComponent<Image>();
        trackImage.Tint.Value = background ?? new color(0.34f, 0.36f, 0.45f, 1f);

        // Filled portion (left to the handle) in the accent color. Spans 0..t of
        // the track, which lines up with the handle since both are inset by the
        // handle radius. Driven live as the value changes.
        var fillSlot = trackSlot.AddSlot("Fill");
        var fillRect = fillSlot.AttachComponent<RectTransform>();
        fillRect.AnchorMin.Value = float2.Zero;
        fillRect.AnchorMax.Value = new float2(initialT, 1f);
        fillRect.OffsetMin.Value = float2.Zero;
        fillRect.OffsetMax.Value = float2.Zero;
        var fillImage = fillSlot.AttachComponent<Image>();
        fillImage.Tint.Value = CurrentStyle.ForegroundColor;
        slider.FillAnchorMaxDrive.DriveTarget(fillRect.AnchorMax);

        // Handle travel area inset by the handle radius so the dot's center
        // ranges over [radius, width-radius] - the dot stays fully on the track
        // at both ends instead of overshooting past them.
        var handleArea = Current.AddSlot("HandleArea");
        var handleAreaRect = handleArea.AttachComponent<RectTransform>();
        handleAreaRect.AnchorMin.Value = float2.Zero;
        handleAreaRect.AnchorMax.Value = float2.One;
        handleAreaRect.OffsetMin.Value = new float2(handleRadius, 0f);
        handleAreaRect.OffsetMax.Value = new float2(-handleRadius, 0f);

        // Round handle, positioned within the travel area.
        var handleSlot = handleArea.AddSlot("Handle");
        var handleRect = handleSlot.AttachComponent<RectTransform>();
        handleRect.AnchorMin.Value = new float2(initialT, 0.5f);
        handleRect.AnchorMax.Value = new float2(initialT, 0.5f);
        handleRect.OffsetMin.Value = new float2(-handleRadius, -handleRadius);
        handleRect.OffsetMax.Value = new float2(handleRadius, handleRadius);
        var handleDot = handleSlot.AttachComponent<ArcSegment>();
        handleDot.AngleStart.Value = 0f;
        handleDot.ArcLength.Value = 360f;
        handleDot.InnerRadius.Value = 0f;
        handleDot.OuterRadius.Value = 9f;
        handleDot.Tint.Value = CurrentStyle.ForegroundColor;
        handleDot.OutlineColor.Value = new color(0.10f, 0.10f, 0.14f, 0.9f);
        handleDot.OutlineThickness.Value = 1.5f;
        slider.AddColorDriver(handleDot.Tint, handleDot.Tint.Value);
        slider.HandleAnchorMinDrive.DriveTarget(handleRect.AnchorMin);
        slider.HandleAnchorMaxDrive.DriveTarget(handleRect.AnchorMax);
        slider.UpdateHandleDrives();

        slider.SetAction(changed);

        return slider;
    }

    // 2D drag pad. initial is a normalized float2 in [0,1] per axis: x runs left
    // (0) -> right (1), y runs bottom (0) -> top (1), so (0,0) is bottom-left.
    public Pad2D Pad2D(float2 initial, Action<Pad2D, float2>? changed = null, color? background = null)
    {
        Next("Pad2D");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? new color(0.34f, 0.36f, 0.45f, 1f);

        var pad = Current.AttachComponent<Pad2D>();
        pad.Value.Value = initial;
        SetElementSize(Current, 96f, 96f);

        float ix = initial.x < 0f ? 0f : (initial.x > 1f ? 1f : initial.x);
        float iy = initial.y < 0f ? 0f : (initial.y > 1f ? 1f : initial.y);
        const float handleRadius = 6f;

        // Small cursor dot anchored at the value; the anchor is driven live and
        // the fixed offsets keep it a constant 12x12 regardless of pad size.
        var handleSlot = Current.AddSlot("Handle");
        var handleRect = handleSlot.AttachComponent<RectTransform>();
        handleRect.AnchorMin.Value = new float2(ix, iy);
        handleRect.AnchorMax.Value = new float2(ix, iy);
        handleRect.OffsetMin.Value = new float2(-handleRadius, -handleRadius);
        handleRect.OffsetMax.Value = new float2(handleRadius, handleRadius);
        var handleImage = handleSlot.AttachComponent<Image>();
        handleImage.Tint.Value = CurrentStyle.ForegroundColor;
        pad.AddColorDriver(handleImage.Tint, handleImage.Tint.Value);
        pad.HandleAnchorMinDrive.DriveTarget(handleRect.AnchorMin);
        pad.HandleAnchorMaxDrive.DriveTarget(handleRect.AnchorMax);
        pad.UpdateHandleDrives();

        pad.SetAction(changed);

        return pad;
    }

    // Editable text field. TextInput resolves a DIRECT child slot literally called "Text" and drives
    // that Text component's content, and it writes the caret and selection indices onto the same
    // component - there is no separate caret slot to build. The child name is load-bearing: rename it
    // and the field goes blank and untypable with no error anywhere. -xlinka
    public TextInput TextInput(string initial = "", string placeholder = "", Action<TextInput, string>? changed = null,
        bool multiline = false, color? background = null)
    {
        Next("TextInput");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        var input = Current.AttachComponent<TextInput>();
        input.Text.Value = initial;
        input.Placeholder.Value = placeholder;
        input.Multiline.Value = multiline;

        Nest();
        // Next("Text") inside the Text builder is what gives the child its required name.
        var text = Text(string.Empty, null, CurrentStyle.TextColor);
        Fill(text.RectTransform!);
        text.WordWrap.Value = multiline;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = multiline ? TextVerticalAlignment.Top : TextVerticalAlignment.Middle;
        text.CaretColor.Value = CurrentStyle.ForegroundColor;
        text.SelectionColor.Value = new color(
            CurrentStyle.ForegroundColor.r, CurrentStyle.ForegroundColor.g, CurrentStyle.ForegroundColor.b, 0.35f);
        NestOut();

        input.SetChangeAction(changed);
        return input;
    }

    // Read-only fill bar. ProgressMeter looks for a direct child called "Fill" first, so the track is
    // just this slot's own Image and the fill is that one child - two Images, no spare slots.
    public ProgressMeter ProgressMeter(float value = 0f, color? track = null, color? fill = null)
    {
        Next("ProgressMeter");
        var trackImage = Current.AttachComponent<Image>();
        trackImage.Tint.Value = track ?? CurrentStyle.BackgroundColor;

        float progress = value < 0f ? 0f : (value > 1f ? 1f : value);

        var fillSlot = Current.AddSlot("Fill");
        var fillRect = fillSlot.AttachComponent<RectTransform>();
        fillRect.AnchorMin.Value = float2.Zero;
        fillRect.AnchorMax.Value = new float2(progress, 1f);
        fillRect.OffsetMin.Value = float2.Zero;
        fillRect.OffsetMax.Value = float2.Zero;
        var fillImage = fillSlot.AttachComponent<Image>();
        fillImage.Tint.Value = fill ?? CurrentStyle.ForegroundColor;

        var meter = Current.AttachComponent<ProgressMeter>();
        meter.Progress.Value = progress;
        return meter;
    }

    // Accordion row: a clickable header strip over a body that hides. The section drives the direct
    // "Content" child's active state and the "Indicator" child's, so both names matter.
    //
    // The indicator is driven straight off Expanded, which means it is the OPEN marker and is gone
    // while the section is shut - the section has one indicator drive, not a pair, so there is nothing
    // to show a closed state with. -xlinka
    //
    // content comes back for the caller to nest into; it already carries a VerticalLayout.
    public CollapsibleSection CollapsibleSection(string header, out RectTransform content, bool expanded = true,
        color? background = null)
    {
        Next("CollapsibleSection");
        var backing = Current.AttachComponent<Image>();
        backing.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        var section = Current.AttachComponent<CollapsibleSection>();
        section.Expanded.Value = expanded;

        float headerHeight = CurrentStyle.FontSize + 14f;
        const float indicatorWidth = 20f;

        var headerSlot = Current.AddSlot("Header");
        var headerRect = headerSlot.AttachComponent<RectTransform>();
        headerRect.AnchorMin.Value = new float2(0f, 1f);
        headerRect.AnchorMax.Value = new float2(1f, 1f);
        headerRect.OffsetMin.Value = new float2(0f, -headerHeight);
        headerRect.OffsetMax.Value = float2.Zero;
        var headerImage = headerSlot.AttachComponent<Image>();
        headerImage.Tint.Value = CurrentStyle.BackgroundColor;
        // Button.OnAttach picks up the Image already on the slot for its hover/press tint, so the
        // image goes on first.
        var headerButton = headerSlot.AttachComponent<Button>();
        headerButton.SetAction(section.OnHeaderPressed);

        var labelSlot = headerSlot.AddSlot("Label");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;
        labelRect.OffsetMin.Value = new float2(indicatorWidth, 0f);
        labelRect.OffsetMax.Value = float2.Zero;
        var labelText = labelSlot.AttachComponent<Text>();
        labelText.Content.Value = header;
        labelText.Size.Value = CurrentStyle.FontSize;
        labelText.Color.Value = CurrentStyle.TextColor;
        labelText.Font.Target = CurrentStyle.Font!;
        labelText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        var indicatorSlot = headerSlot.AddSlot("Indicator");
        var indicatorRect = indicatorSlot.AttachComponent<RectTransform>();
        indicatorRect.AnchorMin.Value = new float2(0f, 0f);
        indicatorRect.AnchorMax.Value = new float2(0f, 1f);
        indicatorRect.OffsetMin.Value = float2.Zero;
        indicatorRect.OffsetMax.Value = new float2(indicatorWidth, 0f);
        var indicatorText = indicatorSlot.AttachComponent<Text>();
        indicatorText.Content.Value = "▾";
        indicatorText.Size.Value = CurrentStyle.FontSize;
        indicatorText.Color.Value = CurrentStyle.TextColor;
        indicatorText.Font.Target = CurrentStyle.Font!;
        indicatorText.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        indicatorText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        indicatorSlot.ActiveSelf.Value = expanded;

        var contentSlot = Current.AddSlot("Content");
        content = contentSlot.AttachComponent<RectTransform>();
        content.AnchorMin.Value = float2.Zero;
        content.AnchorMax.Value = float2.One;
        content.OffsetMin.Value = float2.Zero;
        content.OffsetMax.Value = new float2(0f, -headerHeight);
        contentSlot.AttachComponent<VerticalLayout>();
        contentSlot.ActiveSelf.Value = expanded;

        return section;
    }

    public ScrollRect ScrollRect(out RectTransform content, float2? sensitivity = null, color? background = null,
        bool fitVertical = true)
    {
        Next("ScrollRect");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        var mask = Current.AttachComponent<Mask>();
        mask.ShowMaskGraphic.Value = true;
        var scroll = Current.AttachComponent<ScrollRect>();
        scroll.ScrollSensitivity.Value = sensitivity ?? float2.One;

        var contentSlot = Current.AddSlot("Content");
        content = contentSlot.AttachComponent<RectTransform>();
        scroll.Content.Target = content;
        if (fitVertical)
        {
            // EXACTLY what the working scrollers do (code editor / session / the helio scroll test): top-anchored
            // content, pin OffsetMin.y to the content height so it overflows the viewport. A ScrollContentSizer
            // keeps that pin updated from the content layout's summed height (the rows are dynamic). It ONLY writes
            // the anchor offset - NOT a ContentSizeFitter, which also overrides the computed rect + re-anchors every
            // row, and that extra churn re-meshes the content chunk and kills the scroll's clip_offset. -xlinka
            content.AnchorMin.Value = new float2(0f, 1f);
            content.AnchorMax.Value = new float2(1f, 1f);
            content.OffsetMin.Value = new float2(0f, -100f);
            content.OffsetMax.Value = float2.Zero;
            contentSlot.AttachComponent<ScrollContentSizer>();
        }
        else
        {
            Fill(content);
        }
        // Render-offset scrolling needs the content in its own chunk so it can slide independently of the
        // fixed viewport background/mask (which stay in the parent chunk). Flag ScrollContent at BUILD time so
        // the very first bake already knows (full geometry, no clip elision) - waiting for the first ApplyScroll
        // to set it left the first bake clip-elided when the viewport wasn't laid out yet. -xlinka
        if (Canvas.ScrollRenderOffset)
        {
            var contentChunk = contentSlot.AttachComponent<GraphicChunkRoot>();
            contentChunk.ScrollContent = true;
        }
        return scroll;
    }

    public T HorizontalElementWithLabel<T>(string label, float separation, Func<T> elementBuilder, float gap = 0.01f)
        where T : Component
    {
        Panel();
        Nest();
        var splits = SplitHorizontally(separation, gap, 1f - separation - gap);
        NestInto(splits[0]);
        var labelText = Text(label);
        // Default RectTransform is a 100x100 centered chunk; explicitly fill the split column
        // and middle-align so the label sits flush left of its column and centered vertically. - xlinka
        Fill(labelText.RectTransform!);
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        NestOut();
        NestInto(splits[2]);
        T result = elementBuilder();
        NestOut();
        NestOut();
        return result;
    }

    public Slot Spacer(string name = "Spacer")
    {
        Next(name);
        return Current;
    }

    private void SetupCurrentLayoutElement()
    {
        if (Root.GetComponent<HorizontalLayout>() == null &&
            Root.GetComponent<VerticalLayout>() == null &&
            Root.GetComponent<GridLayout>() == null)
        {
            return;
        }

        var layout = Current.GetComponent<LayoutElement>() ?? Current.AttachComponent<LayoutElement>();
        layout.MinWidth.Value = CurrentStyle.MinWidth;
        layout.MinHeight.Value = CurrentStyle.MinHeight;
        layout.PreferredWidth.Value = CurrentStyle.PreferredWidth;
        layout.PreferredHeight.Value = CurrentStyle.PreferredHeight;
        layout.FlexibleWidth.Value = CurrentStyle.FlexibleWidth;
        layout.FlexibleHeight.Value = CurrentStyle.FlexibleHeight;
        layout.UseZeroMetrics.Value = CurrentStyle.UseZeroMetrics;
    }

    private static void SetElementSize(Slot slot, float width, float height)
    {
        var layout = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        layout.MinWidth.Value = width;
        layout.PreferredWidth.Value = width;
        layout.FlexibleWidth.Value = 0f;
        layout.MinHeight.Value = height;
        layout.PreferredHeight.Value = height;
        layout.FlexibleHeight.Value = 0f;
    }

    private static void SetPadding(LayoutController layout, float padding)
    {
        if (padding == 0f) return;
        if (layout is HorizontalLayout h)
        {
            h.PaddingLeft.Value = padding;
            h.PaddingRight.Value = padding;
            h.PaddingTop.Value = padding;
            h.PaddingBottom.Value = padding;
        }
        else if (layout is VerticalLayout v)
        {
            v.PaddingLeft.Value = padding;
            v.PaddingRight.Value = padding;
            v.PaddingTop.Value = padding;
            v.PaddingBottom.Value = padding;
        }
        else if (layout is GridLayout g)
        {
            g.PaddingLeft.Value = padding;
            g.PaddingRight.Value = padding;
            g.PaddingTop.Value = padding;
            g.PaddingBottom.Value = padding;
        }
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static void Normalize(float[] values)
    {
        if (values.Length == 0) return;

        float sum = 0f;
        for (int i = 0; i < values.Length; i++)
        {
            sum += MathF.Max(values[i], 0f);
        }

        if (sum <= 0f)
        {
            float equal = 1f / values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = equal;
            }
            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = MathF.Max(values[i], 0f) / sum;
        }
    }
}
