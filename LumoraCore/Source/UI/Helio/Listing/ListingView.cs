// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Listing;

// Virtualized list over a ScrollRect viewport: only the rows the viewport can see exist as slots, and
// they are recycled as you scroll rather than destroyed and rebuilt.
//
// Rows are FREE-ANCHORED inside the content, not stacked by a VerticalLayout. That is deliberate: the
// scroll POSITION lives in the content chunk's clip_offset (render-offset scrolling), the content's
// SIZE is pinned here, and a layout controller arranging recycled rows would fight both. Every row's
// top edge comes straight out of the offsets table, so a recycled row lands exactly where its new
// index says it does. -xlinka
//
// Each row is its own GraphicChunkRoot: hover re-meshes one row instead of the page, and a row's
// surface count stays well inside the per-chunk render-priority band. Virtualization keeps the number
// of live chunks bounded by the viewport, so a 500-row source costs the same as a 20-row one.
[SingleInstancePerSlot]
[ComponentCategory("UI/Helio")]
public sealed class ListingView : Component
{
    private const float PlaceEpsilon = 0.01f;
    // Rows either side of the visible band. Two is enough to hide a fast flick without doubling the
    // realized set.
    private const int DefaultOverscan = 2;
    private const int PageSize = 128;
    // A viewport still at the RectTransform 100x100 default has not been laid out; sizing the window
    // off it realizes a handful of rows that the next pass corrects. Harmless, but do not let a zero
    // through or the window collapses to nothing.
    private const float MinViewportHeight = 32f;

    private sealed class RowHandle
    {
        public ListingRowTemplate Template = null!;
        public ListingRow Row = null!;
        public Slot Slot = null!;
        public RectTransform Rect = null!;
        public BorderedImage? Background;
        public int Index = -1;
    }

    public ListingStyle Style { get; private set; } = new ListingStyle();
    public ListingTemplateMapper Templates { get; private set; } = new ListingTemplateMapper();
    public IListingSource? Source { get; private set; }

    public int Overscan { get; set; } = DefaultOverscan;
    public float Spacing { get; set; } = 6f;
    public float SidePadding { get; set; }

    public event Action<ListingView, ListingItem>? ItemActivated;
    public event Action<ListingView, IReadOnlyList<string>>? PathChanged;

    private ScrollRect? _scroll;
    private RectTransform? _viewportRect;
    private Slot? _contentSlot;
    private RectTransform? _contentRect;

    private readonly List<string> _path = new();
    private string _search = string.Empty;
    private string _selectedKey = string.Empty;

    private readonly List<ListingItem> _items = new();
    private readonly List<ListingItem> _fetchBuffer = new();
    // _tops[i] is row i's top edge measured down from the content top; _tops[Extent] is the total
    // content height (no trailing spacing).
    private readonly List<float> _tops = new();

    private readonly Dictionary<int, RowHandle> _live = new();
    private readonly Dictionary<ListingRowTemplate, Stack<RowHandle>> _pool = new();
    private readonly List<RowHandle> _spare = new();

    private int _declaredTotal = -1;
    private bool _sourcePending;
    private bool _endOfData;
    private bool _itemsDirty = true;
    private bool _rebindDirty;
    private int _firstRealized;
    private int _lastRealized = -1;
    private int _pooledCount;

    public IReadOnlyList<string> Path => _path;
    public int ItemCount => _items.Count;
    public int Extent => _declaredTotal >= 0 ? _declaredTotal : _items.Count;
    public int LiveRowCount => _live.Count;
    public int PooledRowCount => _pooledCount;
    public float ContentHeight => _tops.Count == 0 ? 0f : _tops[_tops.Count - 1];
    public ScrollRect? Scroll => _scroll;
    public Slot? ContentSlot => _contentSlot;

    public string Search
    {
        get => _search;
        set
        {
            value ??= string.Empty;
            if (_search == value)
                return;
            _search = value;
            ResetToTop();
        }
    }

    public string SelectedKey
    {
        get => _selectedKey;
        set
        {
            value ??= string.Empty;
            if (_selectedKey == value)
                return;
            _selectedKey = value;
            _rebindDirty = true;
        }
    }

    // Builds the viewport (mask + scroll) and the scrolled content under host, then attaches the view.
    // host keeps whatever layout metrics its parent gave it; nothing here writes the host's rect.
    public static ListingView Attach(Slot host, ListingStyle style, ListingTemplateMapper templates, IListingSource source)
    {
        var viewportRect = host.GetComponent<RectTransform>() ?? host.AttachComponent<RectTransform>();
        if (host.GetComponent<Mask>() == null)
            host.AttachComponent<Mask>();
        var scroll = host.GetComponent<ScrollRect>() ?? host.AttachComponent<ScrollRect>();
        scroll.ScrollSensitivity.Value = new float2(1f, 1f);

        var contentSlot = host.AddSlot("Content");
        var contentRect = contentSlot.AttachComponent<RectTransform>();
        contentRect.AnchorMin.Value = new float2(0f, 1f);
        contentRect.AnchorMax.Value = new float2(1f, 1f);
        contentRect.OffsetMin.Value = new float2(0f, -MinViewportHeight);
        contentRect.OffsetMax.Value = float2.Zero;
        if (Canvas.ScrollRenderOffset)
        {
            var chunk = contentSlot.AttachComponent<GraphicChunkRoot>();
            chunk.ScrollContent = true;
        }
        scroll.Content.Target = contentRect;

        var view = host.GetComponent<ListingView>() ?? host.AttachComponent<ListingView>();
        view.Style = style;
        view.Templates = templates;
        view._scroll = scroll;
        view._viewportRect = viewportRect;
        view._contentSlot = contentSlot;
        view._contentRect = contentRect;
        view.Spacing = style.RowSpacing;
        view.SidePadding = style.RowPadding;
        view.SetSource(source);
        return view;
    }

    public void SetSource(IListingSource? source)
    {
        if (Source != null)
            Source.Changed -= OnSourceChanged;
        Source = source;
        if (Source != null)
            Source.Changed += OnSourceChanged;
        ResetToTop();
    }

    public override void OnDestroy()
    {
        if (Source != null)
            Source.Changed -= OnSourceChanged;
        Source = null;
        base.OnDestroy();
    }

    public void SetPath(params string[] path)
    {
        _path.Clear();
        if (path != null)
            _path.AddRange(path);
        ResetToTop();
        PathChanged?.Invoke(this, _path);
    }

    public void OpenCategory(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        _path.Add(key);
        ResetToTop();
        PathChanged?.Invoke(this, _path);
    }

    public bool NavigateUp()
    {
        if (_path.Count == 0)
            return false;
        _path.RemoveAt(_path.Count - 1);
        ResetToTop();
        PathChanged?.Invoke(this, _path);
        return true;
    }

    // Item set changed under us. Keeps the scroll where it is (it is normalized, so it holds its
    // proportional place) and rebinds every live row: indices shift, so binding by index is the only
    // correct answer after an insert or a remove. -xlinka
    public void Refresh()
    {
        _itemsDirty = true;
        _rebindDirty = true;
    }

    // Value-only refresh: no structural work, just re-read every visible row's accessors.
    public void RefreshValues() => _rebindDirty = true;

    public void ResetToTop()
    {
        _itemsDirty = true;
        _rebindDirty = true;
        if (_scroll != null)
            _scroll.NormalizedPosition = float2.Zero;
    }

    public void Activate(ListingItem? item)
    {
        if (item == null || !item.Interactable)
            return;
        SelectedKey = item.Key;
        if (item is ListingCategory category)
        {
            OpenCategory(category.Key);
            return;
        }
        ItemActivated?.Invoke(this, item);
    }

    public ListingItem? ItemAt(int index)
        => index >= 0 && index < _items.Count ? _items[index] : null;

    private void OnSourceChanged(IListingSource source, ListingChangeEvent change)
    {
        switch (change.Change)
        {
            case ListingChange.Updated:
                _rebindDirty = true;
                break;
            default:
                _itemsDirty = true;
                _rebindDirty = true;
                break;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (Source == null || _contentRect == null || _contentSlot == null || _scroll == null)
            return;
        if (_contentSlot.IsDestroyed)
            return;

        if (_itemsDirty)
        {
            _itemsDirty = false;
            _items.Clear();
            _sourcePending = false;
            _endOfData = false;
            _declaredTotal = Source.TotalCount(new ListingQuery(_path, _search));
            RebuildOffsets();
        }

        Reconcile();
    }

    private void Reconcile()
    {
        int extent = Extent;
        if (extent <= 0)
        {
            ReleaseAll();
            PinContentHeight(0f);
            _firstRealized = 0;
            _lastRealized = -1;
            return;
        }

        ComputeWindow(out int first, out int last);
        // Materializing can grow the extent when the source never told us a total, which moves every
        // offset below it - re-derive the window once rather than realizing against a stale table.
        int before = _items.Count;
        EnsureMaterialized(last);
        if (_items.Count != before)
        {
            RebuildOffsets();
            ComputeWindow(out first, out last);
        }

        PinContentHeight(ContentHeight);

        if (!_rebindDirty && first == _firstRealized && last == _lastRealized)
            return;

        bool rebind = _rebindDirty;
        _rebindDirty = false;
        _firstRealized = first;
        _lastRealized = last;

        _spare.Clear();
        foreach (var pair in _live)
        {
            if (pair.Key < first || pair.Key > last)
                _spare.Add(pair.Value);
        }
        for (int i = 0; i < _spare.Count; i++)
            _live.Remove(_spare[i].Index);

        for (int index = first; index <= last; index++)
        {
            // Past what the source has handed over yet (a page still in flight). Leave the gap: the
            // row appears when the page lands and Changed brings us back.
            if (index >= _items.Count)
                continue;

            var item = _items[index];
            var template = Templates.Resolve(item);
            if (template == null)
                continue;

            if (_live.TryGetValue(index, out var existing))
            {
                if (ReferenceEquals(existing.Template, template))
                {
                    Place(existing, index);
                    if (rebind)
                        BindRow(existing, item);
                    continue;
                }
                _live.Remove(index);
                _spare.Add(existing);
            }

            var handle = TakeSpare(template) ?? TakePooled(template) ?? BuildRow(template);
            handle.Index = index;
            _live[index] = handle;
            if (!handle.Slot.ActiveSelf.Value)
                handle.Slot.ActiveSelf.Value = true;
            Place(handle, index);
            BindRow(handle, item);
        }

        for (int i = 0; i < _spare.Count; i++)
        {
            var handle = _spare[i];
            if (handle.Index >= 0 && _live.TryGetValue(handle.Index, out var claimed) && ReferenceEquals(claimed, handle))
                continue;
            Recycle(handle);
        }
        _spare.Clear();
    }

    private void ComputeWindow(out int first, out int last)
    {
        int extent = Extent;
        float viewportHeight = MathF.Max(MinViewportHeight, _viewportRect?.LocalComputeRect.height ?? 0f);
        float scrollY = _scroll?.AbsolutePosition.y ?? 0f;
        if (scrollY < 0f)
            scrollY = 0f;

        first = IndexAt(scrollY) - Overscan;
        last = IndexAt(scrollY + viewportHeight) + Overscan;
        if (first < 0) first = 0;
        if (last > extent - 1) last = extent - 1;
        if (last < first) last = first;
    }

    // Largest index whose top edge is at or above y.
    private int IndexAt(float y)
    {
        int extent = Extent;
        if (extent <= 0 || _tops.Count < 2)
            return 0;
        int low = 0;
        int high = extent - 1;
        while (low < high)
        {
            int mid = (low + high + 1) >> 1;
            if (_tops[mid] <= y)
                low = mid;
            else
                high = mid - 1;
        }
        return low;
    }

    private void EnsureMaterialized(int throughIndex)
    {
        if (Source == null || _sourcePending || _endOfData)
            return;
        if (_declaredTotal >= 0 && _items.Count >= _declaredTotal)
            return;

        int guard = 0;
        while (_items.Count <= throughIndex && guard++ < 64)
        {
            _fetchBuffer.Clear();
            var query = new ListingQuery(_path, _search, _items.Count, PageSize);
            if (Source.Fetch(query, _fetchBuffer) == ListingFetch.Pending)
            {
                _sourcePending = true;
                return;
            }
            if (_fetchBuffer.Count == 0)
            {
                _endOfData = true;
                return;
            }
            _items.AddRange(_fetchBuffer);
            if (_fetchBuffer.Count < PageSize)
            {
                _endOfData = true;
                return;
            }
            if (_declaredTotal >= 0 && _items.Count >= _declaredTotal)
                return;
        }
    }

    private void RebuildOffsets()
    {
        _tops.Clear();
        int extent = Extent;
        float y = 0f;
        for (int i = 0; i < extent; i++)
        {
            _tops.Add(y);
            // Un-materialized rows measure at the mapper's default; the table re-derives once their
            // page lands, so the extent is only ever briefly approximate.
            float height = i < _items.Count ? Templates.HeightFor(_items[i]) : Templates.DefaultHeight;
            y += height + Spacing;
        }
        _tops.Add(extent > 0 ? MathF.Max(0f, y - Spacing) : 0f);
    }

    private float HeightOf(int index)
    {
        int extent = Extent;
        if (index < 0 || index >= extent)
            return Templates.DefaultHeight;
        if (index + 1 < extent)
            return _tops[index + 1] - _tops[index] - Spacing;
        return _tops[extent] - _tops[index];
    }

    private void Place(RowHandle handle, int index)
    {
        float top = _tops[index];
        float height = HeightOf(index);
        var rect = handle.Rect;
        // Top-anchored strip: OffsetMax.y is the top edge (negative = down from the content top),
        // OffsetMin.y the bottom. Same convention the session scrollbar handle uses.
        var anchorMin = new float2(0f, 1f);
        var anchorMax = new float2(1f, 1f);
        if (!rect.AnchorMin.Value.Equals(anchorMin))
            rect.AnchorMin.Value = anchorMin;
        if (!rect.AnchorMax.Value.Equals(anchorMax))
            rect.AnchorMax.Value = anchorMax;

        var offsetMax = new float2(-SidePadding, -top);
        var offsetMin = new float2(SidePadding, -(top + height));
        // Sync writes have no equality gate: an unchanged write still dirties the rect and re-meshes
        // this row's chunk, which on a stationary list is a re-tessellation per frame for nothing.
        if (!Approximately(rect.OffsetMax.Value, offsetMax))
            rect.OffsetMax.Value = offsetMax;
        if (!Approximately(rect.OffsetMin.Value, offsetMin))
            rect.OffsetMin.Value = offsetMin;
    }

    private void PinContentHeight(float total)
    {
        var rect = _contentRect;
        if (rect == null)
            return;
        float pinned = MathF.Max(total, MinViewportHeight);
        var offsetMin = rect.OffsetMin.Value;
        if (MathF.Abs(offsetMin.y + pinned) > PlaceEpsilon)
            rect.OffsetMin.Value = new float2(0f, -pinned);
        var offsetMax = rect.OffsetMax.Value;
        if (offsetMax.y != 0f || offsetMax.x != 0f)
            rect.OffsetMax.Value = float2.Zero;
    }

    private void BindRow(RowHandle handle, ListingItem item)
    {
        var row = handle.Row;
        row.Item = item;
        row.Selected = _selectedKey.Length > 0 && string.Equals(_selectedKey, item.Key, StringComparison.Ordinal);
        if (handle.Background != null && !handle.Background.IsDestroyed)
        {
            ListingStyle.SetTint(handle.Background, !item.Interactable
                ? Style.DisabledFill
                : row.Selected ? Style.RowSelectedFill : Style.RowFill);
        }
        row.Bind(item);
        row.OnSelectionChanged();
    }

    private RowHandle? TakeSpare(ListingRowTemplate template)
    {
        for (int i = 0; i < _spare.Count; i++)
        {
            var handle = _spare[i];
            if (!ReferenceEquals(handle.Template, template))
                continue;
            _spare.RemoveAt(i);
            return handle;
        }
        return null;
    }

    private RowHandle? TakePooled(ListingRowTemplate template)
    {
        if (!_pool.TryGetValue(template, out var stack) || stack.Count == 0)
            return null;
        var handle = stack.Pop();
        _pooledCount--;
        return handle;
    }

    private void Recycle(RowHandle handle)
    {
        handle.Index = -1;
        handle.Row.Unbind();
        handle.Row.Item = null;
        handle.Row.Selected = false;
        if (handle.Slot != null && !handle.Slot.IsDestroyed && handle.Slot.ActiveSelf.Value)
            handle.Slot.ActiveSelf.Value = false;
        if (!_pool.TryGetValue(handle.Template, out var stack))
        {
            stack = new Stack<RowHandle>();
            _pool[handle.Template] = stack;
        }
        stack.Push(handle);
        _pooledCount++;
    }

    private void ReleaseAll()
    {
        if (_live.Count == 0)
            return;
        _spare.Clear();
        foreach (var pair in _live)
            _spare.Add(pair.Value);
        _live.Clear();
        for (int i = 0; i < _spare.Count; i++)
            Recycle(_spare[i]);
        _spare.Clear();
    }

    private RowHandle BuildRow(ListingRowTemplate template)
    {
        var slot = _contentSlot!.AddSlot("Row");
        var rect = slot.AttachComponent<RectTransform>();
        // Per-row chunk: see the class note. Rows are built lazily here, which is exactly the case the
        // canvas warns about - attaching components raises MarkStructuralDirty, so the next root pass
        // discovers the chunk and meshes it. -xlinka
        slot.AttachComponent<GraphicChunkRoot>();
        var background = Style.ApplyPanel(slot, Style.RowFill, Style.RowBorder);
        if (!template.UsesRowBackground)
        {
            background.Tint.Value = color.Transparent;
            background.BorderTint.Value = color.Transparent;
        }
        template.ConfigureRow(slot, Style);

        var handle = new RowHandle
        {
            Template = template,
            Slot = slot,
            Rect = rect,
            Background = template.UsesRowBackground ? background : null,
        };

        var builder = Style.RowBuilder(slot);
        var row = template.Build(this, builder, slot);
        row.View = this;
        row.Slot = slot;
        handle.Row = row;
        return handle;
    }

    private static bool Approximately(in float2 a, in float2 b)
        => MathF.Abs(a.x - b.x) <= PlaceEpsilon && MathF.Abs(a.y - b.y) <= PlaceEpsilon;
}
