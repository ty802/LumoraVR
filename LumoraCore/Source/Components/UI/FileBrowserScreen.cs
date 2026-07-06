// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public sealed class FileBrowserScreen : DashboardScreen, IDashboardKeyInput
{
    private const int GridColumns = 8;
    private const float CardHeight = 64f;
    private const float CardSpacing = 6f;
    private const float ContentPad = 6f;

    private static readonly color FolderColor = new color(0.95f, 0.78f, 0.28f, 0.40f);
    private static readonly color FileColor = new color(0.40f, 0.46f, 0.62f, 0.40f);
    private static readonly color RowBorder = new color(0.52f, 0.46f, 0.82f, 0.35f);
    private static readonly color ViewportFill = new color(0.16f, 0.15f, 0.24f, 0.30f);
    private static readonly color BarFill = new color(0.16f, 0.15f, 0.24f, 0.45f);
    private static readonly color ToolFill = new color(0.22f, 0.20f, 0.34f, 0.55f);
    private static readonly color TextPrimary = new color(0.93f, 0.93f, 0.97f, 1f);
    private static readonly color TextDim = new color(0.70f, 0.70f, 0.78f, 1f);
    private static readonly color ScrollHandleColor = new color(0.55f, 0.50f, 0.85f, 0.90f);

    private const float LaidOutViewportFloor = 100f;

    private string _currentPath = string.Empty;
    private string? _selectedPath;
    private string _search = string.Empty;
    private Text? _pathLabel;
    private Text? _statusLabel;
    private Text? _searchLabel;
    private Slot? _contentSlot;
    private RectTransform? _contentRect;
    private Dashboard? _dashboard;

    private ScrollRect? _scroll;
    private RectTransform? _viewportRect;
    private Slot? _scrollTrack;
    private RectTransform? _scrollHandle;
    private float _handlePressY;
    private float _handlePressScroll;
    private int _scrollHandleRetries;

    private bool _newFolderActive;
    private string _newFolderName = string.Empty;
    private Slot? _newFolderSlot;
    private Text? _newFolderNameText;
    private Text? _newFolderErrorText;
    private readonly Dictionary<Slot, ItemData> _items = new();
    private readonly List<Entry> _entries = new();

    private readonly struct Entry
    {
        public readonly string Path;
        public readonly string Name;
        public readonly string NameLower;
        public readonly bool IsDirectory;
        public Entry(string path, bool isDirectory)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path) ?? path;
            NameLower = Name.ToLowerInvariant();
            IsDirectory = isDirectory;
        }
    }

    private readonly struct ItemData
    {
        public readonly string Path;
        public readonly bool IsDirectory;
        public readonly BorderedImage Background;
        public readonly color BaseColor;
        public ItemData(string path, bool isDirectory, BorderedImage bg, color baseColor)
        {
            Path = path;
            IsDirectory = isDirectory;
            Background = bg;
            BaseColor = baseColor;
        }
    }

    protected override void BuildContent(UIBuilder builder)
    {
        _dashboard = FindDashboard();
        var font = _dashboard?.Font.Target;
        var rounded = _dashboard?.RoundedSprite;

        var root = builder.Current;
        var v = root.AttachComponent<VerticalLayout>();
        v.Spacing.Value = 8f;
        v.PaddingLeft.Value = 12f;
        v.PaddingRight.Value = 12f;
        v.PaddingTop.Value = 12f;
        v.PaddingBottom.Value = 12f;
        v.ForceExpandWidth.Value = true;
        v.ForceExpandHeight.Value = false;

        BuildToolbar(root, font, rounded);
        BuildSearchBar(root, font, rounded);
        BuildViewport(root, font, rounded);
        BuildStatusBar(root, font, rounded);
        BuildNewFolderModal(root, font, rounded);

        string initial;
        try { initial = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { initial = string.Empty; }
        if (string.IsNullOrEmpty(initial) || !Directory.Exists(initial))
            initial = Directory.GetCurrentDirectory();
        NavigateTo(initial);
    }

    private Dashboard? FindDashboard()
    {
        var s = Slot;
        while (s != null)
        {
            var d = s.GetComponent<Dashboard>();
            if (d != null) return d;
            s = s.Parent;
        }
        return null;
    }

    private void BuildToolbar(Slot root, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded)
    {
        var toolbar = root.AddSlot("Toolbar");
        toolbar.AttachComponent<RectTransform>();
        var le = toolbar.AttachComponent<LayoutElement>();
        le.MinHeight.Value = 40f;
        le.PreferredHeight.Value = 40f;
        var h = toolbar.AttachComponent<HorizontalLayout>();
        h.Spacing.Value = 6f;
        h.ForceExpandHeight.Value = true;
        h.ForceExpandWidth.Value = false;

        AddToolButton(toolbar, "Up", GoUp, font, rounded, 56f);

        var pathSlot = toolbar.AddSlot("Path");
        pathSlot.AttachComponent<RectTransform>();
        var pathLE = pathSlot.AttachComponent<LayoutElement>();
        pathLE.FlexibleWidth.Value = 1f;
        pathLE.MinHeight.Value = 40f;
        pathLE.PreferredHeight.Value = 40f;
        var pathBg = pathSlot.AttachComponent<BorderedImage>();
        ApplyRounded(pathBg, BarFill, RowBorder, rounded);
        var pathBuilder = new UIBuilder(pathSlot);
        pathBuilder.Font(font).FontSize(13f);
        _pathLabel = pathBuilder.Text(string.Empty, 13f, TextPrimary);
        _pathLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _pathLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        FillRect(_pathLabel.RectTransform!, 14f, 14f, 0f, 0f);

        AddToolButton(toolbar, "Refresh", Refresh, font, rounded, 90f);
        AddToolButton(toolbar, "+ Folder", OpenNewFolderDialog, font, rounded, 110f);
        AddToolButton(toolbar, "Open Folder", OpenFolderHere, font, rounded, 120f);
    }

    private void BuildSearchBar(Slot root, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded)
    {
        var bar = root.AddSlot("Search");
        bar.AttachComponent<RectTransform>();
        var le = bar.AttachComponent<LayoutElement>();
        le.MinHeight.Value = 36f;
        le.PreferredHeight.Value = 36f;
        var bg = bar.AttachComponent<BorderedImage>();
        ApplyRounded(bg, BarFill, RowBorder, rounded);

        var b = new UIBuilder(bar);
        b.Font(font).FontSize(13f);
        _searchLabel = b.Text("Search… (type to filter)", 13f, TextDim);
        _searchLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _searchLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        FillRect(_searchLabel.RectTransform!, 14f, 14f, 0f, 0f);
    }

    private void BuildViewport(Slot root, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded)
    {
        // Viewport + scrollbar sit side by side. The scrollbar's handle-drag sets the scroll position
        // directly (the file grid is a wall of card buttons, so grabbing empty space to drag-scroll or
        // relying on the wheel alone is unreliable - every other scrolling dash screen has one). -xlinka
        var area = root.AddSlot("ViewportArea");
        area.AttachComponent<RectTransform>();
        var areaLE = area.AttachComponent<LayoutElement>();
        areaLE.FlexibleHeight.Value = 1f;
        areaLE.MinHeight.Value = 200f;
        var areaLayout = area.AttachComponent<HorizontalLayout>();
        areaLayout.Spacing.Value = 6f;
        areaLayout.ForceExpandWidth.Value = false;
        areaLayout.ForceExpandHeight.Value = true;

        var viewport = area.AddSlot("Viewport");
        _viewportRect = viewport.AttachComponent<RectTransform>();
        var le = viewport.AttachComponent<LayoutElement>();
        le.FlexibleWidth.Value = 1f;
        le.FlexibleHeight.Value = 1f;

        var viewportBg = viewport.AttachComponent<BorderedImage>();
        ApplyRounded(viewportBg, ViewportFill, RowBorder, rounded);

        viewport.AttachComponent<Mask>();
        _scroll = viewport.AttachComponent<ScrollRect>();
        _scroll.ScrollSensitivity.Value = new float2(1f, 1f);
        _scroll.ScrollChanged += (_, _) => UpdateScrollHandle();

        _contentSlot = viewport.AddSlot("Content");
        _contentRect = _contentSlot.AttachComponent<RectTransform>();
        _contentRect.AnchorMin.Value = new float2(0f, 1f);
        _contentRect.AnchorMax.Value = new float2(1f, 1f);
        _contentRect.OffsetMin.Value = new float2(0f, -CardHeight);
        _contentRect.OffsetMax.Value = new float2(0f, 0f);

        var grid = _contentSlot.AttachComponent<GridLayout>();
        grid.Columns.Value = GridColumns;
        grid.Spacing.Value = CardSpacing;
        grid.PaddingLeft.Value = ContentPad;
        grid.PaddingRight.Value = ContentPad;
        grid.PaddingTop.Value = ContentPad;
        grid.PaddingBottom.Value = ContentPad;

        _scroll.Content.Target = _contentRect;

        BuildScrollbar(area, rounded);

        _ = font;
    }

    private void BuildScrollbar(Slot area, RoundedRectTextureProvider? rounded)
    {
        var track = area.AddSlot("Scrollbar");
        _scrollTrack = track;
        track.AttachComponent<RectTransform>();
        // Own graphic chunk: the handle's position is rewritten every scroll frame (UpdateScrollHandle sets its
        // RectTransform offsets). Without a chunk boundary here that layout change finds no owning chunk and
        // escalates to a FULL canvas rebuild - re-tessellating the whole file grid every frame (the scroll
        // flicker + fps drop). With its own chunk the move re-meshes just this tiny track+handle. -xlinka
        track.AttachComponent<GraphicChunkRoot>();
        var trackLE = track.AttachComponent<LayoutElement>();
        trackLE.MinWidth.Value = 16f;
        trackLE.PreferredWidth.Value = 16f;
        trackLE.FlexibleWidth.Value = 0f;
        trackLE.FlexibleHeight.Value = 1f;
        var trackBg = track.AttachComponent<BorderedImage>();
        ApplyRounded(trackBg, BarFill, RowBorder, rounded);

        var handleSlot = track.AddSlot("Handle");
        _scrollHandle = handleSlot.AttachComponent<RectTransform>();
        _scrollHandle.AnchorMin.Value = new float2(0f, 1f);
        _scrollHandle.AnchorMax.Value = new float2(1f, 1f);
        _scrollHandle.OffsetMin.Value = new float2(2f, -60f);
        _scrollHandle.OffsetMax.Value = new float2(-2f, 0f);
        var handleBg = handleSlot.AttachComponent<BorderedImage>();
        ApplyRounded(handleBg, ScrollHandleColor, new color(0f, 0f, 0f, 0f), rounded);

        var interaction = handleSlot.AttachComponent<InteractionElement>();
        interaction.Pressed += OnHandlePress;
        interaction.Dragged += OnHandleDrag;
    }

    private void OnHandlePress(UIInteractionContext context)
    {
        _handlePressY = context.LocalPoint.y;
        _handlePressScroll = _scroll?.AbsolutePosition.y ?? 0f;
    }

    private void OnHandleDrag(UIInteractionContext context)
    {
        if (_scroll == null || _viewportRect == null || _contentRect == null)
            return;
        float viewportHeight = _viewportRect.LocalComputeRect.height;
        float contentHeight = _contentRect.LocalComputeRect.height;
        if (viewportHeight <= LaidOutViewportFloor || contentHeight <= 0f)
            return;
        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0f)
            return;
        float handleHeight = MathF.Max(30f, viewportHeight * (viewportHeight / contentHeight));
        float travel = viewportHeight - handleHeight;
        if (travel <= 0f)
            return;
        // Dragging the handle down (local Y decreases) scrolls the content down.
        float deltaY = context.LocalPoint.y - _handlePressY;
        float scrolled = _handlePressScroll - deltaY * (maxScroll / travel);
        // Setting AbsolutePosition routes through ScrollRect -> ApplyScrollOffset (moves the content chunk +
        // requests a repaint) and fires ScrollChanged -> UpdateScrollHandle. No explicit canvas dirty: the old
        // MarkDirty() here was the parameterless FULL-rebuild, which re-tessellated the whole grid on every drag
        // frame (the drag flicker + fps drop). -xlinka
        _scroll.AbsolutePosition = new float2(0f, System.Math.Clamp(scrolled, 0f, maxScroll));
    }

    private void UpdateScrollHandle()
    {
        if (_scroll == null || _scrollTrack == null || _scrollHandle == null
            || _viewportRect == null || _contentRect == null)
            return;
        float viewportHeight = _viewportRect.LocalComputeRect.height;
        if (viewportHeight <= LaidOutViewportFloor)
        {
            if (_scrollHandleRetries++ < 30)
                World?.RunInUpdates(1, UpdateScrollHandle);
            return;
        }
        _scrollHandleRetries = 0;

        float contentHeight = _contentRect.LocalComputeRect.height;
        if (contentHeight <= 0f)
        {
            if (_scrollHandleRetries++ < 30)
                World?.RunInUpdates(1, UpdateScrollHandle);
            return;
        }

        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        // Gate the ActiveSelf writes: Sync.Value is NOT change-gated, so setting it to its current value still
        // fires a change -> the track participates in a layout, so that's MarkVisibilityDirty() (root re-mesh +
        // reconcile). UpdateScrollHandle runs every scroll frame, so an unconditional write here was a full-ish
        // rebuild per frame (the scroll flicker + fps drop). Only write on an actual transition. -xlinka
        if (maxScroll <= 0.5f)
        {
            if (_scrollTrack.ActiveSelf.Value)
                _scrollTrack.ActiveSelf.Value = false; // everything fits; hide the bar
            if (_scroll.NormalizedPosition.y != 0f)
                _scroll.NormalizedPosition = float2.Zero;
            return;
        }
        if (!_scrollTrack.ActiveSelf.Value)
            _scrollTrack.ActiveSelf.Value = true;
        if (_scroll.AbsolutePosition.y > maxScroll)
            _scroll.AbsolutePosition = new float2(0f, maxScroll);
        float handleHeight = MathF.Max(30f, viewportHeight * (viewportHeight / contentHeight));
        float fraction = System.Math.Clamp(_scroll.AbsolutePosition.y / maxScroll, 0f, 1f);
        float offset = fraction * (viewportHeight - handleHeight);
        _scrollHandle.OffsetMax.Value = new float2(-2f, -offset);
        _scrollHandle.OffsetMin.Value = new float2(2f, -(offset + handleHeight));
    }

    private void BuildStatusBar(Slot root, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded)
    {
        var status = root.AddSlot("Status");
        status.AttachComponent<RectTransform>();
        var le = status.AttachComponent<LayoutElement>();
        le.MinHeight.Value = 40f;
        le.PreferredHeight.Value = 40f;
        var h = status.AttachComponent<HorizontalLayout>();
        h.Spacing.Value = 6f;
        h.ForceExpandHeight.Value = true;

        var infoSlot = status.AddSlot("Info");
        infoSlot.AttachComponent<RectTransform>();
        var infoLE = infoSlot.AttachComponent<LayoutElement>();
        infoLE.FlexibleWidth.Value = 1f;
        infoLE.MinHeight.Value = 40f;
        infoLE.PreferredHeight.Value = 40f;
        var infoBg = infoSlot.AttachComponent<BorderedImage>();
        ApplyRounded(infoBg, BarFill, RowBorder, rounded);
        var ib = new UIBuilder(infoSlot);
        ib.Font(font).FontSize(13f);
        _statusLabel = ib.Text("Select a file…", 13f, TextDim);
        _statusLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _statusLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        FillRect(_statusLabel.RectTransform!, 14f, 14f, 0f, 0f);

        AddToolButton(status, "Import", Import, font, rounded, 96f);
    }

    private static void AddToolButton(Slot parent, string label, Action onClick, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded, float width)
    {
        var slot = parent.AddSlot(label);
        slot.AttachComponent<RectTransform>();
        var le = slot.AttachComponent<LayoutElement>();
        if (width > 0f)
        {
            le.MinWidth.Value = width;
            le.PreferredWidth.Value = width;
        }
        else
        {
            le.MinWidth.Value = 80f;
            le.FlexibleWidth.Value = 1f;
        }
        le.MinHeight.Value = 40f;
        le.PreferredHeight.Value = 40f;
        var img = slot.AttachComponent<BorderedImage>();
        ApplyRounded(img, ToolFill, RowBorder, rounded);
        var b = new UIBuilder(slot);
        b.Font(font).FontSize(13f);
        var t = b.Text(label, 13f, TextPrimary);
        t.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        t.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        FillRect(t.RectTransform!, 0f, 0f, 0f, 0f);
        var btn = slot.AttachComponent<Button>();
        btn.Clicked += (_, _) => onClick();
        btn.AddColorDriver(img.Tint, ToolFill, InteractionColorMode.Direct);
    }

    private static void ApplyRounded(BorderedImage img, color tint, color border, RoundedRectTextureProvider? rounded)
    {
        img.Tint.Value = tint;
        img.BorderTint.Value = border;
        img.BorderThickness.Value = 2f;
        if (rounded != null)
        {
            img.Texture.Target = rounded;
            img.NineSlice.Value = true;
            img.Borders.Value = new float4(12f, 12f, 12f, 12f);
        }
    }

    private static void FillRect(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(left, bottom);
        rect.OffsetMax.Value = new float2(-right, -top);
    }

    private void NavigateTo(string path)
    {
        _currentPath = path;
        _selectedPath = null;
        SetSearch(string.Empty);
        if (_pathLabel != null) _pathLabel.Content.Value = path;
        if (_statusLabel != null)
        {
            _statusLabel.Content.Value = "Select a file…";
            _statusLabel.Color.Value = TextDim;
        }
        Refresh();
    }

    public void SetSearch(string text)
    {
        text ??= string.Empty;
        if (_search == text) return;
        _search = text;
        if (_searchLabel != null)
        {
            if (text.Length == 0)
            {
                _searchLabel.Content.Value = "Search… (type to filter)";
                _searchLabel.Color.Value = TextDim;
            }
            else
            {
                _searchLabel.Content.Value = text;
                _searchLabel.Color.Value = TextPrimary;
            }
        }
        ApplyFilter();
    }

    private void GoUp()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        try
        {
            var parent = Path.GetDirectoryName(_currentPath);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                NavigateTo(parent);
        }
        catch { }
    }

    private void Refresh()
    {
        if (_contentSlot == null) return;

        _entries.Clear();

        string[] directories = Array.Empty<string>();
        string[] files = Array.Empty<string>();
        try
        {
            if (Directory.Exists(_currentPath))
            {
                directories = Directory.GetDirectories(_currentPath);
                files = Directory.GetFiles(_currentPath);
            }
        }
        catch (Exception ex)
        {
            if (_statusLabel != null)
                _statusLabel.Content.Value = "Error: " + ex.Message;
            SetContentHeight(0);
            return;
        }

        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories) _entries.Add(new Entry(dir, isDirectory: true));
        foreach (var file in files) _entries.Add(new Entry(file, isDirectory: false));

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_contentSlot == null) return;

        var toRemove = new List<Slot>(_contentSlot.Children);
        foreach (var s in toRemove) s.Destroy();
        _items.Clear();

        var font = _dashboard?.Font.Target;
        var rounded = _dashboard?.RoundedSprite;
        var filter = _search.ToLowerInvariant();

        int visible = 0;
        foreach (var e in _entries)
        {
            if (filter.Length > 0 && !e.NameLower.Contains(filter))
                continue;
            AddCard(e.Name, e.Path, e.IsDirectory, font, rounded);
            visible++;
        }

        int rows = (visible + GridColumns - 1) / GridColumns;
        if (rows < 1) rows = 1;
        SetContentHeight(rows * CardHeight + (rows - 1) * CardSpacing + ContentPad * 2f);

        // Refresh the scrollbar now and again once the new content height has been laid out (the second
        // pass also re-touches the scroll so ScrollRect recomputes its range against the fresh rects).
        UpdateScrollHandle();
        World?.RunInUpdates(2, UpdateScrollHandle);
    }

    private void SetContentHeight(float height)
    {
        if (_contentRect == null) return;
        _contentRect.OffsetMin.Value = new float2(0f, -height);
        _contentRect.OffsetMax.Value = new float2(0f, 0f);
    }

    private void AddCard(string label, string fullPath, bool isDirectory, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded)
    {
        if (_contentSlot == null) return;

        var slot = _contentSlot.AddSlot(label);
        slot.AttachComponent<RectTransform>();

        var bg = slot.AttachComponent<BorderedImage>();
        var baseColor = isDirectory ? FolderColor : GetFileClassColor(fullPath);
        ApplyRounded(bg, baseColor, RowBorder, rounded);

        var nameBuilder = new UIBuilder(slot);
        nameBuilder.Font(font).FontSize(11f);
        var nameText = nameBuilder.Text(TruncateLabel(label), 11f, TextPrimary);
        nameText.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        nameText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        nameText.WordWrap.Value = true;
        FillRect(nameText.RectTransform!, 4f, 4f, 4f, 4f);

        var btn = slot.AttachComponent<Button>();
        bool isDir = isDirectory;
        string path = fullPath;
        btn.Clicked += (_, _) =>
        {
            if (isDir) NavigateTo(path);
            else Select(slot, path);
        };

        _items[slot] = new ItemData(fullPath, isDirectory, bg, baseColor);
    }

    private void Select(Slot itemSlot, string path)
    {
        _selectedPath = path;
        foreach (var pair in _items)
        {
            bool sel = ReferenceEquals(pair.Key, itemSlot);
            pair.Value.Background.Tint.Value = sel ? Highlight(pair.Value.BaseColor) : pair.Value.BaseColor;
        }
        if (_statusLabel != null)
        {
            _statusLabel.Color.Value = TextPrimary;
            try
            {
                var info = new FileInfo(path);
                _statusLabel.Content.Value = $"{Path.GetFileName(path)}  ·  {FormatBytes(info.Length)}";
            }
            catch
            {
                _statusLabel.Content.Value = Path.GetFileName(path) ?? path;
            }
        }
    }

    private void Import()
    {
        if (string.IsNullOrEmpty(_selectedPath)) return;
        if (World == null) return;
        SpawnImport(_selectedPath);
    }

    private void OpenFolderHere()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        if (World == null) return;
        SpawnImport(_currentPath);
    }

    private void SpawnImport(string path)
    {
        var (position, rotation) = ResolveSpawnPose();
        Lumora.Core.Components.Import.UniversalImporter.Import(path, World, position, rotation);
    }

    private (float3 position, floatQ rotation) ResolveSpawnPose()
    {
        // Prefer the local user's head - that's where they're looking, so dialogs/spawned
        // items land in front of them. Fall back to the dashboard panel's transform if
        // no head is registered yet (loading screens, etc). - xlinka
        var head = World?.LocalUser?.Root?.HeadSlot;
        if (head != null)
        {
            // View direction is the head's -Z (float3.Backward). Using +Z put the dialog BEHIND the
            // user, so they'd turn around and see its back ("wrong way round"). - xlinka
            var fwd = head.GlobalRotation * float3.Backward;
            return (head.GlobalPosition + fwd * 0.75f, head.GlobalRotation);
        }
        var basePos = Slot?.GlobalPosition ?? float3.Zero;
        var baseRot = Slot?.GlobalRotation ?? floatQ.Identity;
        var forward = baseRot * float3.Forward;
        return (basePos + forward * 1.5f, baseRot);
    }

    private void BuildNewFolderModal(Slot root, IAssetProvider<FontSet>? font, RoundedRectTextureProvider? rounded)
    {
        var modal = root.AddSlot("NewFolderModal");
        var modalRect = modal.AttachComponent<RectTransform>();
        modalRect.AnchorMin.Value = float2.Zero;
        modalRect.AnchorMax.Value = float2.One;
        modalRect.OffsetMin.Value = float2.Zero;
        modalRect.OffsetMax.Value = float2.Zero;
        modal.AttachComponent<IgnoreLayout>();
        modal.ActiveSelf.Value = false;
        _newFolderSlot = modal;

        var backdrop = modal.AttachComponent<Image>();
        backdrop.Tint.Value = new color(0f, 0f, 0f, 0.78f);
        modal.AttachComponent<InteractionBlock>();

        var panel = modal.AddSlot("Panel");
        var panelRect = panel.AttachComponent<RectTransform>();
        panelRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        panelRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        panelRect.OffsetMin.Value = new float2(-180f, -110f);
        panelRect.OffsetMax.Value = new float2(180f, 110f);

        var panelBg = panel.AttachComponent<BorderedImage>();
        panelBg.Tint.Value = new color(0.10f, 0.09f, 0.16f, 1f);
        panelBg.BorderTint.Value = new color(0.62f, 0.56f, 0.92f, 0.95f);
        panelBg.BorderThickness.Value = 2f;
        if (rounded != null)
        {
            panelBg.Texture.Target = rounded;
            panelBg.NineSlice.Value = true;
            panelBg.Borders.Value = new float4(12f, 12f, 12f, 12f);
        }

        var layout = panel.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 10f;
        layout.PaddingLeft.Value = 18f;
        layout.PaddingRight.Value = 18f;
        layout.PaddingTop.Value = 18f;
        layout.PaddingBottom.Value = 18f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        AddDialogRow(panel, "Title", 26f, b =>
        {
            var t = b.Text("Create new folder", 16f, TextPrimary);
            t.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
            t.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            FillRect(t.RectTransform!, 0f, 0f, 0f, 0f);
        }, font, 16f);

        var inputSlot = panel.AddSlot("Input");
        inputSlot.AttachComponent<RectTransform>();
        var inputLE = inputSlot.AttachComponent<LayoutElement>();
        inputLE.MinHeight.Value = 40f;
        inputLE.PreferredHeight.Value = 40f;
        var inputBg = inputSlot.AttachComponent<BorderedImage>();
        ApplyRounded(inputBg, BarFill, RowBorder, rounded);
        var inputBuilder = new UIBuilder(inputSlot);
        inputBuilder.Font(font).FontSize(14f);
        _newFolderNameText = inputBuilder.Text("Folder name…", 14f, TextDim);
        _newFolderNameText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _newFolderNameText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        FillRect(_newFolderNameText.RectTransform!, 14f, 14f, 0f, 0f);

        AddDialogRow(panel, "Error", 18f, b =>
        {
            _newFolderErrorText = b.Text(string.Empty, 11f, new color(1f, 0.45f, 0.45f, 1f));
            _newFolderErrorText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
            _newFolderErrorText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            FillRect(_newFolderErrorText.RectTransform!, 0f, 0f, 0f, 0f);
        }, font, 11f);

        var spacer = panel.AddSlot("Spacer");
        spacer.AttachComponent<RectTransform>();
        spacer.AttachComponent<LayoutElement>().FlexibleHeight.Value = 1f;

        var btnRow = panel.AddSlot("Buttons");
        btnRow.AttachComponent<RectTransform>();
        var btnRowLE = btnRow.AttachComponent<LayoutElement>();
        btnRowLE.MinHeight.Value = 40f;
        btnRowLE.PreferredHeight.Value = 40f;
        var bh = btnRow.AttachComponent<HorizontalLayout>();
        bh.Spacing.Value = 8f;
        bh.ForceExpandWidth.Value = true;
        bh.ForceExpandHeight.Value = true;

        AddToolButton(btnRow, "Cancel", CloseNewFolderDialog, font, rounded, 0f);
        AddToolButton(btnRow, "Create", ConfirmNewFolder, font, rounded, 0f);
    }

    private static void AddDialogRow(Slot panel, string name, float height, Action<UIBuilder> build, IAssetProvider<FontSet>? font, float fontSize)
    {
        var row = panel.AddSlot(name);
        row.AttachComponent<RectTransform>();
        var le = row.AttachComponent<LayoutElement>();
        le.MinHeight.Value = height;
        le.PreferredHeight.Value = height;
        var b = new UIBuilder(row);
        b.Font(font).FontSize(fontSize);
        build(b);
    }

    private void OpenNewFolderDialog()
    {
        if (_newFolderSlot == null) return;
        _newFolderActive = true;
        _newFolderName = string.Empty;
        if (_newFolderErrorText != null) _newFolderErrorText.Content.Value = string.Empty;
        UpdateNewFolderUI();
        _newFolderSlot.ActiveSelf.Value = true;
    }

    private void CloseNewFolderDialog()
    {
        _newFolderActive = false;
        _newFolderName = string.Empty;
        if (_newFolderSlot != null) _newFolderSlot.ActiveSelf.Value = false;
    }

    private void ConfirmNewFolder()
    {
        var name = _newFolderName.Trim();
        if (string.IsNullOrWhiteSpace(name)) { ShowNewFolderError("Name cannot be empty."); return; }
        if (name.Length > 255) { ShowNewFolderError("Name is too long."); return; }
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            if (name.IndexOf(ch) >= 0) { ShowNewFolderError("Name has invalid characters."); return; }
        }
        try
        {
            var fullPath = Path.Combine(_currentPath, name);
            if (Directory.Exists(fullPath)) { ShowNewFolderError("Folder already exists."); return; }
            Directory.CreateDirectory(fullPath);
        }
        catch (Exception ex)
        {
            ShowNewFolderError(ex.Message);
            return;
        }
        CloseNewFolderDialog();
        Refresh();
    }

    private void ShowNewFolderError(string msg)
    {
        if (_newFolderErrorText != null) _newFolderErrorText.Content.Value = msg;
    }

    private void UpdateNewFolderUI()
    {
        if (_newFolderNameText == null) return;
        if (_newFolderName.Length == 0)
        {
            _newFolderNameText.Content.Value = "Folder name…";
            _newFolderNameText.Color.Value = TextDim;
        }
        else
        {
            _newFolderNameText.Content.Value = _newFolderName;
            _newFolderNameText.Color.Value = TextPrimary;
        }
    }

    public bool ConsumeChar(char c)
    {
        if (!_newFolderActive) return false;
        if (char.IsControl(c)) return true;
        _newFolderName += c;
        UpdateNewFolderUI();
        return true;
    }

    public bool ConsumeBackspace()
    {
        if (!_newFolderActive) return false;
        if (_newFolderName.Length > 0)
            _newFolderName = _newFolderName.Substring(0, _newFolderName.Length - 1);
        UpdateNewFolderUI();
        return true;
    }

    public bool ConsumeEnter()
    {
        if (!_newFolderActive) return false;
        ConfirmNewFolder();
        return true;
    }

    public bool ConsumeEscape()
    {
        if (!_newFolderActive) return false;
        CloseNewFolderDialog();
        return true;
    }

    private static color Highlight(in color c)
    {
        return new color(
            c.r + (1f - c.r) * 0.35f,
            c.g + (1f - c.g) * 0.35f,
            c.b + (1f - c.b) * 0.35f,
            c.a + (1f - c.a) * 0.55f);
    }

    private static color GetFileClassColor(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" or ".tga" or ".tif" or ".tiff"
                => new color(0.45f, 0.85f, 0.55f, 0.45f),
            ".mp3" or ".wav" or ".ogg" or ".flac" or ".m4a" or ".aac" or ".opus"
                => new color(0.85f, 0.55f, 0.95f, 0.45f),
            ".mp4" or ".mov" or ".avi" or ".webm" or ".mkv" or ".m4v" or ".wmv"
                => new color(0.95f, 0.45f, 0.55f, 0.45f),
            ".obj" or ".fbx" or ".glb" or ".gltf" or ".dae" or ".blend" or ".stl"
                => new color(0.95f, 0.62f, 0.25f, 0.45f),
            ".cs" or ".py" or ".js" or ".ts" or ".cpp" or ".c" or ".h" or ".hpp" or ".java" or ".go" or ".rs" or ".rb" or ".lua" or ".swift" or ".kt"
                => new color(0.45f, 0.65f, 0.95f, 0.45f),
            ".txt" or ".md" or ".rst" or ".log"
                => new color(0.85f, 0.85f, 0.95f, 0.45f),
            ".pdf" or ".doc" or ".docx" or ".rtf" or ".odt"
                => new color(0.95f, 0.75f, 0.55f, 0.45f),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz"
                => new color(0.70f, 0.55f, 0.35f, 0.45f),
            ".exe" or ".bat" or ".sh" or ".msi" or ".cmd" or ".com" or ".app" or ".dll"
                => new color(0.95f, 0.40f, 0.40f, 0.45f),
            ".ttf" or ".otf" or ".woff" or ".woff2"
                => new color(0.95f, 0.85f, 0.45f, 0.45f),
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".cfg" or ".conf"
                => new color(0.55f, 0.85f, 0.85f, 0.45f),
            _ => FileColor,
        };
    }

    private static string TruncateLabel(string label)
    {
        const int Max = 16;
        if (label.Length <= Max) return label;
        return label.Substring(0, Max - 1) + "…";
    }

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / (double)GB:0.0} GB";
        if (bytes >= MB) return $"{bytes / (double)MB:0.0} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:0.0} KB";
        return $"{bytes} B";
    }
}
