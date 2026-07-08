// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Text;
using System.Threading.Tasks;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.UI;

// scrollable monospace gdshader editor. two Text layers share the same rect: the front layer shows
// the syntax-highlighted copy (rich text), the back layer is the real editable field whose glyphs
// are transparent but whose caret and selection still draw. the user types raw source into the back
// layer; each edit re-highlights the front layer. Apply saves the source as a local asset and
// repoints the material's shader provider at it, which re-runs the sandbox gate and reparses uniforms.
// The back layer renders behind so selection sits under the colored glyphs and the caret shows through the
// gaps between them - standard editor layering. The highlighter's tag output strips back to exactly the raw
// text, so the colored glyphs line up under the invisible editable ones (and thus under the caret). -xlinka
[ComponentCategory("Utility/Inspectors")]
public sealed class CodeEditor : Component
{
    private const float CodeFontSize = 15f;
    private const float CodeLineHeight = 22f; // approximate FiraCode line advance at CodeFontSize
    private static readonly color CodeBackground = new(0.05f, 0.05f, 0.08f, 1f);
    private static readonly color DefaultText = new(0.84f, 0.87f, 0.94f, 1f);
    private static readonly color CaretColor = new(0.85f, 0.9f, 1f, 1f);
    private static readonly color SelectionColor = new(0.30f, 0.45f, 0.85f, 0.40f);
    private static readonly color PassColor = new(0.50f, 0.85f, 0.55f, 1f);

    private readonly SyncRef<TextInput> _input;
    private readonly SyncRef<Text> _highlight;
    private readonly SyncRef<Text> _status;
    private readonly SyncRef<RectTransform> _contentRect;
    private readonly SyncRef<CustomShaderMaterial> _material;
    // Standalone mode only: null when embedded (auto-grow), which builds no inner scroll at all.
    private readonly SyncRef<ScrollRect> _scroll;

    private Action<string>? _textHandler;

    // Auto-grow mode (embedded in a scrolling inspector): the editor sizes itself to fit ALL its lines and builds
    // NO inner ScrollRect, so the parent panel's scroll owns the whole block - no wheel to trap, no scroll chunk
    // nested inside another one. Off = standalone editor with its own inner scroll. -xlinka
    private bool _autoGrow;
    private LayoutElement? _scrollHostLE;
    private LayoutElement? _selfLE;

    public CodeEditor()
    {
        _input = new SyncRef<TextInput>(this);
        _highlight = new SyncRef<Text>(this);
        _status = new SyncRef<Text>(this);
        _contentRect = new SyncRef<RectTransform>(this);
        _material = new SyncRef<CustomShaderMaterial>(this);
        _scroll = new SyncRef<ScrollRect>(this);
    }

    // builds the editor into this component's slot, loaded with the material's current source
    public void Setup(CustomShaderMaterial material, Slot themeContext, bool autoGrow = false)
    {
        _autoGrow = autoGrow;
        _material.Target = material;
        string source = material?.Shader.Asset?.Source ?? string.Empty;
        var font = themeContext.GetComponentInParent<UITheme>()?.ThemeFont;

        var col = Slot.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 3f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        // Embedded: the editor reports its full height to the parent layout so it takes exactly the room it needs.
        if (_autoGrow)
            _selfLE = Slot.GetComponent<LayoutElement>() ?? Slot.AttachComponent<LayoutElement>();

        BuildScrollArea(source, font);
        BuildToolbar(themeContext, source);

        HookInput();
        RefreshHighlight();
    }

    private void BuildScrollArea(string source, IAssetProvider<FontSet>? font)
    {
        var host = Slot.AddSlot("Scroll");
        host.AttachComponent<RectTransform>();
        var le = host.AttachComponent<LayoutElement>();
        // Standalone: flex to fill. Embedded (auto-grow): pinned to the content height by UpdateContentHeight so
        // the box exactly fits its text and there is nothing left to scroll past. -xlinka
        le.FlexibleHeight.Value = _autoGrow ? 0f : 1f;
        le.MinHeight.Value = 220f;
        _scrollHostLE = le;

        var bg = host.AttachComponent<Image>();
        bg.Tint.Value = CodeBackground;
        host.AttachComponent<Mask>().ShowMaskGraphic.Value = true;
        // Embedded: NO inner ScrollRect. Auto-grow sizes the box to fit every line, so it can never scroll -
        // and a ScrollRect's content is its own SCROLL chunk, whose viewport window then has to ride the outer
        // panel's scroll offset. A material clip rect is a fixed canvas-space window and can't ride a live
        // offset, so the code got sliced by a window it had already slid out of (the old "scroll and see one
        // line" bug). Without the inner ScrollRect the box mask is locked to the content and clips as a bake
        // trim, which is exact at any outer scroll position. -xlinka
        if (!_autoGrow)
        {
            var scroll = host.AttachComponent<ScrollRect>();
            scroll.ScrollSensitivity.Value = new float2(1f, 1f);
            _scroll.Target = scroll;
        }

        // Top-pinned, full-width content strip; its HEIGHT is set explicitly from the line count - the scroll
        // range standalone, the block height embedded (an explicit pin, not a ContentSizeFitter, which
        // rewrites the same rect the scroll owns). -xlinka
        var content = host.AddSlot("Content");
        var contentRect = content.AttachComponent<RectTransform>();
        contentRect.AnchorMin.Value = new float2(0f, 1f);
        contentRect.AnchorMax.Value = new float2(1f, 1f);
        contentRect.OffsetMin.Value = new float2(0f, -300f);
        contentRect.OffsetMax.Value = float2.Zero;
        if (_scroll.Target != null)
        {
            _scroll.Target.Content.Target = contentRect;
        }
        else
        {
            // Keep the code text in its OWN chunk anyway: a keystroke re-highlights the whole block, and
            // without a chunk boundary that re-meshes the entire panel body instead of just this box. Plain
            // chunk root (not scroll content) so it inherits the panel's scroll offset and nothing else. -xlinka
            content.AttachComponent<GraphicChunkRoot>();
        }
        _contentRect.Target = contentRect;

        // Editable layer (back): a hit surface + TextInput + its child "Text" carrying caret/selection.
        var editableSlot = content.AddSlot("Editable");
        FillParent(editableSlot.AttachComponent<RectTransform>());
        // Transparent hit surface: TextInput is itself an InteractionElement and hit-tests on its own
        // rect, so this Image only needs to exist, not be opaque. An OPAQUE fill here draws OVER the
        // colored highlight layer (same rect, and image-vs-text ordering isn't guaranteed) and hides
        // the code - the dark background comes from the Scroll viewport's own bg Image. -xlinka
        var hitSurface = editableSlot.AttachComponent<Image>();
        hitSurface.Tint.Value = new color(0f, 0f, 0f, 0f);
        var input = editableSlot.AttachComponent<TextInput>();
        input.Multiline.Value = true;
        _input.Target = input;

        var textSlot = editableSlot.AddSlot("Text");
        FillParent(textSlot.AttachComponent<RectTransform>());
        var editable = textSlot.AttachComponent<Text>();
        ConfigureCodeText(editable, font);
        editable.Color.Value = new color(0f, 0f, 0f, 0f); // glyphs invisible; the front layer supplies color
        editable.CaretColor.Value = CaretColor;
        editable.SelectionColor.Value = SelectionColor;

        // Highlight layer (front): the read-only colored copy.
        var highlightSlot = content.AddSlot("Highlight");
        FillParent(highlightSlot.AttachComponent<RectTransform>());
        var highlight = highlightSlot.AttachComponent<Text>();
        ConfigureCodeText(highlight, font);
        highlight.Color.Value = DefaultText;
        highlight.RichText.Value = true;
        _highlight.Target = highlight;

        input.Text.Value = source;
        UpdateContentHeight(source);
    }

    private static void ConfigureCodeText(Text text, IAssetProvider<FontSet>? font)
    {
        text.Size.Value = CodeFontSize;
        text.WordWrap.Value = false; // code lines don't wrap; overflow clips against the mask
        text.RichText.Value = false;
        text.LineSpacing.Value = 1f;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Top;
        if (font != null)
            text.Font.Target = font;
    }

    private void BuildToolbar(Slot themeContext, string source)
    {
        InspectorUI.FixedRow(Slot, "Toolbar", 28f, out var ui, themeContext);

        ui.PushStyle();
        ui.MinWidth(88f);
        ui.PreferredWidth(88f);
        ui.FlexibleWidth(0f);
        ui.TextColor(InspectorUI.AccentColor);
        var applyBtn = ui.Button("Apply", null!);
        applyBtn.SetAction(OnApplyPressed);
        var btnText = applyBtn.Slot.GetComponentInChildren<Text>();
        if (btnText != null)
            InspectorUI.FillParent(btnText.RectTransform!);
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var status = ui.Text("", InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(status.RectTransform!);
        status.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        status.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _status.Target = status;
        ui.PopStyle();

        ShowVerdict(ShaderSourceValidator.Validate(source));
    }

    private void HookInput()
    {
        var input = _input.Target;
        if (input == null)
            return;
        // Re-highlight, and (embedded) re-grow to fit the new line count so the panel's scroll range keeps up.
        _textHandler = s => { if (!IsDestroyed) { RefreshHighlight(); if (_autoGrow) UpdateContentHeight(s); } };
        input.Text.OnChanged += _textHandler;
    }

    public override void OnDestroy()
    {
        var input = _input.Target;
        if (input != null && _textHandler != null)
            input.Text.OnChanged -= _textHandler;
        _textHandler = null;
        base.OnDestroy();
    }

    private void RefreshHighlight()
    {
        var input = _input.Target;
        var highlight = _highlight.Target;
        if (input == null || highlight == null || highlight.IsDestroyed)
            return;
        string src = input.Text.Value ?? string.Empty;
        highlight.Content.Value = GdShaderHighlighter.Highlight(src);
        UpdateContentHeight(src);
    }

    // Pin the content height to the line count so the ScrollRect can reach the last line.
    private void UpdateContentHeight(string src)
    {
        var rect = _contentRect.Target;
        if (rect == null || rect.IsDestroyed)
            return;
        int lines = 1;
        for (int i = 0; i < src.Length; i++)
            if (src[i] == '\n') lines++;
        float height = (lines + 1) * CodeLineHeight + 12f;
        if (height < 80f)
            height = 80f;
        rect.OffsetMin.Value = new float2(0f, -height);
        rect.OffsetMax.Value = float2.Zero;

        if (_autoGrow)
        {
            // Size the box to the text (nothing to scroll past) and report the whole block's height to the
            // parent so the outer panel scrolls through it as one. -xlinka
            if (_scrollHostLE != null && !_scrollHostLE.IsDestroyed)
            {
                _scrollHostLE.MinHeight.Value = height;
                _scrollHostLE.PreferredHeight.Value = height;
            }
            if (_selfLE != null && !_selfLE.IsDestroyed)
            {
                float full = height + 28f + 3f; // toolbar row (28) + column spacing (3)
                _selfLE.MinHeight.Value = full;
                _selfLE.PreferredHeight.Value = full;
            }
        }
    }

    [SyncMethod]
    public void OnApplyPressed(Button button, UIInteractionContext context) => Apply();

    // validates the edited source, shows the verdict, and writes it back to the material
    public void Apply()
    {
        var input = _input.Target;
        var material = _material.Target;
        if (input == null || material == null || material.IsDestroyed)
            return;

        string src = input.Text.Value ?? string.Empty;
        ShowVerdict(ShaderSourceValidator.Validate(src));
        // Store + repoint even when rejected, matching import: a rejected shader never compiles, but the
        // stored source lets the inspector show the full report. -xlinka
        _ = ApplyAsync(material, src);
    }

    private async Task ApplyAsync(CustomShaderMaterial material, string src)
    {
        var db = Engine.Current?.LocalDB;
        if (db == null)
        {
            SetStatusOnWorld("no local asset database", InspectorUI.DangerColor);
            return;
        }

        string uri;
        try
        {
            uri = await db.SaveAssetAsync(Encoding.UTF8.GetBytes(src), ".gdshader").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"CodeEditor: failed to store edited shader: {ex.Message}");
            SetStatusOnWorld("save failed", InspectorUI.DangerColor);
            return;
        }

        if (string.IsNullOrEmpty(uri))
        {
            SetStatusOnWorld("save failed", InspectorUI.DangerColor);
            return;
        }

        // Datamodel writes on the world thread; repointing the URL reloads the provider, which propagates
        // to the material and re-runs UpdateMaterial (sandbox gate + uniform reparse). -xlinka
        RunSynchronously(() =>
        {
            if (material.IsDestroyed)
                return;
            var provider = GetOrCreateProvider(material);
            if (provider != null)
                provider.URL.Value = new Uri(uri);
        });
    }

    private static ShaderSourceProvider? GetOrCreateProvider(CustomShaderMaterial material)
    {
        if (material.Shader.Target is ShaderSourceProvider existing && !existing.IsDestroyed)
            return existing;
        var slot = material.Slot;
        if (slot == null)
            return null;
        var provider = slot.AttachComponent<ShaderSourceProvider>();
        material.Shader.Target = provider;
        return provider;
    }

    private void ShowVerdict(ShaderSourceValidator.Result verdict)
    {
        if (verdict.IsValid)
            SetStatus($"sandbox: passed ({(string.IsNullOrEmpty(verdict.ShaderType) ? "?" : verdict.ShaderType)}, {verdict.UniformCount} uniforms)", PassColor);
        else
            SetStatus($"sandbox: REJECTED - {verdict.Errors[0]}", InspectorUI.DangerColor);
    }

    private void SetStatus(string text, color tint)
    {
        var status = _status.Target;
        if (status == null || status.IsDestroyed)
            return;
        status.Content.Value = text;
        status.Color.Value = tint;
    }

    // Marshal a status update onto the world thread (called from the async save continuation).
    private void SetStatusOnWorld(string text, color tint)
        => RunSynchronously(() => SetStatus(text, tint));

    private static void FillParent(RectTransform rect) => InspectorUI.FillParent(rect);
}
