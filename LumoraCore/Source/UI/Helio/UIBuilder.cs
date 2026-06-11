// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
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

    public Button Button(string label, Action<Button, UIInteractionContext>? clicked = null, color? background = null)
    {
        Next("Button");
        var image = Current.AttachComponent<Image>();
        image.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        var button = Current.AttachComponent<Button>();
        if (clicked != null)
        {
            button.Clicked += clicked;
        }

        Nest();
        var text = Text(label, null, CurrentStyle.TextColor);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        Fill(text.RectTransform!);
        NestOut();
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
        var boxImage = boxSlot.AttachComponent<Image>();
        boxImage.Tint.Value = background ?? CurrentStyle.BackgroundColor;
        checkbox.AddColorDriver(boxImage.Tint, boxImage.Tint.Value);

        var checkSlot = boxSlot.AddSlot("Check");
        var checkRect = checkSlot.AttachComponent<RectTransform>();
        checkRect.AnchorMin.Value = new float2(0.25f, 0.25f);
        checkRect.AnchorMax.Value = new float2(0.75f, 0.75f);
        checkRect.OffsetMin.Value = float2.Zero;
        checkRect.OffsetMax.Value = float2.Zero;
        var checkImage = checkSlot.AttachComponent<Image>();
        checkImage.Tint.Value = CurrentStyle.ForegroundColor;
        checkSlot.ActiveSelf.Value = isChecked;
        checkbox.SetCheckVisual(checkSlot.ActiveSelf);

        if (changed != null)
        {
            checkbox.ValueChanged += changed;
        }

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

        var trackSlot = Current.AddSlot("Track");
        var trackRect = trackSlot.AttachComponent<RectTransform>();
        trackRect.AnchorMin.Value = new float2(0f, 0.5f);
        trackRect.AnchorMax.Value = new float2(1f, 0.5f);
        trackRect.OffsetMin.Value = new float2(10f, -2f);
        trackRect.OffsetMax.Value = new float2(-10f, 2f);
        var trackImage = trackSlot.AttachComponent<Image>();
        trackImage.Tint.Value = background ?? new color(0.30f, 0.32f, 0.38f, 0.95f);

        var handleSlot = Current.AddSlot("Handle");
        var handleRect = handleSlot.AttachComponent<RectTransform>();
        float initialT = max > min ? (value - min) / (max - min) : 0f;
        handleRect.AnchorMin.Value = new float2(initialT, 0.5f);
        handleRect.AnchorMax.Value = new float2(initialT, 0.5f);
        handleRect.OffsetMin.Value = new float2(-7f, -9f);
        handleRect.OffsetMax.Value = new float2(7f, 9f);
        var handleImage = handleSlot.AttachComponent<Image>();
        handleImage.Tint.Value = CurrentStyle.ForegroundColor;
        slider.AddColorDriver(handleImage.Tint, handleImage.Tint.Value);
        slider.HandleAnchorMinDrive?.DriveTarget(handleRect.AnchorMin);
        slider.HandleAnchorMaxDrive?.DriveTarget(handleRect.AnchorMax);
        slider.UpdateHandleDrives();

        if (changed != null)
        {
            slider.ValueChanged += changed;
        }

        return slider;
    }

    public ScrollRect ScrollRect(out RectTransform content, float2? sensitivity = null, color? background = null)
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
        Fill(content);
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
