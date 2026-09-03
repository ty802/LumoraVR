// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Components.UI.Worlds;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// What you have saved, on the cloud, as one folder tree you can shape.
//
// One root, and anything under it. The rail on the left shows the way down from the root to the
// folder being looked at and takes you back up with a press; its bottom holds the kind filters and
// the new folder button. One bar above the cards holds the way up, the two ways in from the world
// (what you hold, what you wear), search, sort and Move, and the actions for whatever is selected.
// A card is selected with one press and opened with a second press on the same card. Move lifts a
// thing as a transparent copy that follows the pointer; the next press on a folder card, on a step in
// the rail, or on Up puts it there. Nothing here touches the disk beyond the download cache. -xlinka
[ComponentCategory("Hidden")]
public sealed class InventoryScreen : WidgetScreen, IDashboardKeyInput, ITabbedScreen
{
    private const float BarHeight = 40f;
    private const float StatusHeight = 30f;
    private const int Columns = 6;
    private const int PageSize = 36;   // six full rows
    private const float CardSpacing = 10f;
    private const float ContentPad = 4f;
    private const int BufferRows = 1;
    private const float LaidOutFloor = 20f;
    private const float DoublePressSeconds = 0.45f;
    private const float ConfirmSeconds = 3f;

    private enum Sort { AToZ, ZToA, Newest, Oldest, ByType }
    private enum Show { All = 0, Worlds = 1, Items = 2, Avatars = 3 }
    private enum Field { None, Search, Dialog }
    private enum DialogKind { None, NewFolder, Rename }

    private IInventorySource _source = new CloudInventorySource();

    private BrowserParts _parts = null!;
    private ThumbnailCache _thumbnails = null!;
    private InventorySidebar _sidebar = null!;
    private Slot? _bar;
    private Slot? _statusRow;
    private Text? _status;
    private RectTransform? _viewportRect;
    private RectTransform? _contentRect;
    private Slot? _contentSlot;
    private ScrollRect? _scroll;
    private DashScrollbar? _scrollbar;
    private InventoryMoveCatcher? _catcher;
    private InventoryGhost? _ghost;
    private Slot? _dialogRoot;
    private Text? _dialogTitle;
    private Text? _dialogText;
    private Text? _dialogError;
    private readonly List<InventoryCard> _pool = new();
    private readonly List<(RectTransform rect, string id, string name)> _dropRects = new();
    private int _boundFirstRow = -1;
    private float _lastCardW = -1f;

    // The way down from the root: (folder id, name). The last one is the folder being looked at.
    private readonly List<(string id, string name)> _chain = new() { (Inventory.Root, "Inventory") };
    private string FolderId => _chain[_chain.Count - 1].id;
    private string FolderName => _chain[_chain.Count - 1].name;

    private int _page;
    private Sort _sort = Sort.AToZ;
    private Show _show = Show.All;
    private string _search = string.Empty;
    private Field _focus = Field.None;
    private readonly List<InventoryEntry> _entries = new();
    private readonly List<InventoryEntry> _visible = new();
    private int _loadSerial;
    private bool _loading;
    private bool _busy;

    private string? _selectedId;
    private DateTime _lastPress;
    private string? _lastPressId;

    private bool _pickForMove;
    private InventoryEntry? _carried;
    private string? _dropTarget;
    private float _confirmDeleteLeft;

    private DialogKind _dialog = DialogKind.None;
    private string _dialogBuffer = string.Empty;
    private string? _dialogProblem;

    private string? _notice;
    private color _noticeColor = DashTheme.TextDim;
    private bool _artDirty;
    private readonly HashSet<string> _thumbnailsAsked = new(StringComparer.Ordinal);

    // HARNESS

    // Comma separated so the harness can stack them: "demo,select" looks at the demo tree with a card
    // selected, "demo,carry" mid-move.
    public bool ShowTab(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.Contains(','))
        {
            bool any = false;
            foreach (var part in name.Split(','))
                any |= ShowTab(part.Trim());
            return any;
        }
        switch (name.Trim().ToLowerInvariant())
        {
            case "demo":
                _source = new DemoInventorySource();
                _thumbnailsAsked.Clear();
                NavigateToRoot();
                return true;
            case "select":
                World?.RunInUpdates(120, () =>
                {
                    if (IsDestroyed || _visible.Count == 0) return;
                    int pick = 0;
                    for (int i = 0; i < _visible.Count; i++)
                    {
                        if (!_visible[i].IsFolder) { pick = i; break; }
                    }
                    _selectedId = _visible[pick].Id;
                    RepaintCards();
                    RebuildBar();
                });
                return true;
            case "carry":
                World?.RunInUpdates(120, HarnessCarryNow);
                return true;
            default:
                return false;
        }
    }

    private void HarnessCarryNow()
    {
        if (IsDestroyed || _visible.Count == 0 || _ghost == null || _catcher == null)
            return;
        InventoryEntry? first = null;
        for (int i = 0; i < _visible.Count; i++)
        {
            if (!_visible[i].IsFolder) { first = _visible[i]; break; }
        }
        first ??= _visible[0];
        BeginCarry(first.Value, null);
        if (_pool.Count > 1 && _pool[1].Active)
        {
            var r = _pool[1].Rect.LocalComputeRect;
            var canvas = _dashboard?.Slot.GetComponent<Canvas>();
            var offset = canvas != null ? canvas.ScrollOffsetOf(_pool[1].Root) : float2.Zero;
            _ghost.MoveTo(new float2(r.x + r.width * 0.5f + offset.x, r.y + r.height * 0.5f + offset.y));
            if (_pool[1].Kind == InventoryEntryKind.Folder)
            {
                _dropTarget = _pool[1].Id;
                RepaintCards();
            }
        }
    }

    // LIFECYCLE

    protected override void OnShow()
    {
        base.OnShow();
        Reload();
    }

    public override void OnDestroy()
    {
        _thumbnails?.Clear();
        base.OnDestroy();
    }

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        _parts = new BrowserParts
        {
            Regular = _dashboard?.Font.Target,
            Semibold = _dashboard?.FontSemibold.Target ?? _dashboard?.Font.Target,
            Bold = _dashboard?.FontBold.Target ?? _dashboard?.Font.Target,
        };

        var root = builder.Current;
        var col = root.AttachComponent<VerticalLayout>();
        col.Spacing.Value = DashTheme.Gap;
        col.PaddingLeft.Value = DashTheme.GapLarge;
        col.PaddingRight.Value = DashTheme.GapLarge;
        col.PaddingTop.Value = DashTheme.GapLarge;
        col.PaddingBottom.Value = DashTheme.GapLarge;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        var thumbHost = Slot.AddSlot("Thumbnails");
        _thumbnails = new ThumbnailCache(this, thumbHost);

        _bar = root.AddSlot("Bar");
        _bar.AttachComponent<RectTransform>();
        BrowserParts.Height(_bar, BarHeight);
        var barLayout = _bar.AttachComponent<HorizontalLayout>();
        barLayout.Spacing.Value = 6f;
        barLayout.ForceExpandWidth.Value = false;
        barLayout.ForceExpandHeight.Value = true;

        Rule(root, DashTheme.Divider);

        var body = root.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        var bodyElement = body.AttachComponent<LayoutElement>();
        bodyElement.FlexibleWidth.Value = 1f;
        bodyElement.FlexibleHeight.Value = 1f;
        bodyElement.MinHeight.Value = 200f;
        var bodyLayout = body.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = DashTheme.GapLarge;
        bodyLayout.ForceExpandWidth.Value = false;
        bodyLayout.ForceExpandHeight.Value = true;

        _sidebar = new InventorySidebar(_parts, body, "+ New Folder", () => OpenDialog(DialogKind.NewFolder));
        _sidebar.PathPicked += NavigateBackTo;
        _sidebar.FilterPicked += id => { _show = (Show)id; _sidebar.SelectFilter(id); _page = 0; ApplyView(); };
        _sidebar.AddFilter((int)Show.All, "All");
        _sidebar.AddFilter((int)Show.Worlds, "Worlds");
        _sidebar.AddFilter((int)Show.Items, "Items");
        _sidebar.AddFilter((int)Show.Avatars, "Avatars");
        _sidebar.SelectFilter((int)_show);
        _sidebar.SetPath(_chain);

        // The rail and the grid had nothing between them, so the screen read as one wide field of
        // stuff with a few pills floating at the left. A one-unit column between them is enough to
        // say these are two areas, and being IN the row layout it gets a real rect. -xlinka
        var railEdge = body.AddSlot("RailEdge");
        railEdge.AttachComponent<RectTransform>();
        var railEdgeElement = railEdge.AttachComponent<LayoutElement>();
        railEdgeElement.MinWidth.Value = 1f;
        railEdgeElement.PreferredWidth.Value = 1f;
        railEdgeElement.FlexibleWidth.Value = 0f;
        railEdgeElement.FlexibleHeight.Value = 1f;
        railEdge.AttachComponent<Image>().Tint.Value = DashTheme.OutlineStrong;

        BuildViewport(body);

        Rule(root, DashTheme.Divider);

        _statusRow = root.AddSlot("Status");
        _statusRow.AttachComponent<RectTransform>();
        BrowserParts.Height(_statusRow, StatusHeight);
        var statusLayout = _statusRow.AttachComponent<HorizontalLayout>();
        statusLayout.Spacing.Value = 6f;
        statusLayout.ForceExpandWidth.Value = false;
        statusLayout.ForceExpandHeight.Value = true;

        // The catcher and the ghost come last so they sit above everything in the hit scan and the draw
        // order. Both stay inactive until a move starts.
        var catcherSlot = root.AddSlot("MoveCatcher");
        BrowserParts.Fill(catcherSlot.AttachComponent<RectTransform>());
        catcherSlot.AttachComponent<IgnoreLayout>();
        _catcher = catcherSlot.AttachComponent<InventoryMoveCatcher>();
        _catcher.Moved = OnCarryMoved;
        _catcher.Dropped = OnCarryDropped;
        catcherSlot.ActiveSelf.Value = false;

        var ghostHost = root.AddSlot("GhostHost");
        var ghostRect = ghostHost.AttachComponent<RectTransform>();
        BrowserParts.Fill(ghostRect);
        ghostHost.AttachComponent<IgnoreLayout>();
        _ghost = new InventoryGhost(_parts, ghostHost, ghostRect);

        BuildDialog(root);
        RebuildBar();
        Reload();
    }

    private void BuildViewport(Slot body)
    {
        var host = body.AddSlot("Grid");
        host.AttachComponent<RectTransform>();
        var hostElement = host.AttachComponent<LayoutElement>();
        hostElement.FlexibleWidth.Value = 1f;
        hostElement.FlexibleHeight.Value = 1f;

        var viewport = host.AddSlot("Viewport");
        _viewportRect = viewport.AttachComponent<RectTransform>();
        _viewportRect.AnchorMin.Value = float2.Zero;
        _viewportRect.AnchorMax.Value = float2.One;
        _viewportRect.OffsetMin.Value = float2.Zero;
        _viewportRect.OffsetMax.Value = new float2(-(DashScrollbar.Width + 8f), 0f);
        viewport.AttachComponent<Mask>();
        _scroll = viewport.AttachComponent<ScrollRect>();
        _scroll.ScrollSensitivity.Value = new float2(1f, 1f);
        _scroll.ScrollChanged += (_, _) => { _scrollbar?.Refresh(); RelayoutVirtual(force: false); };

        _contentSlot = viewport.AddSlot("Content");
        _contentRect = _contentSlot.AttachComponent<RectTransform>();
        _contentRect.AnchorMin.Value = new float2(0f, 1f);
        _contentRect.AnchorMax.Value = new float2(1f, 1f);
        _contentRect.OffsetMin.Value = new float2(0f, -InventoryCard.Height);
        _contentRect.OffsetMax.Value = float2.Zero;
        _scroll.Content.Target = _contentRect;

        _scrollbar = DashScrollbar.OnRightEdge(host, 0f, 0f, 0f);
        _scrollbar.Bind(_scroll, _viewportRect, _contentRect, MarkDirty);
    }

    // Results from the cloud arrive on a task thread; everything that touches the tree runs here.
    private void OnUi(Action action)
    {
        var world = World;
        if (world == null || IsDestroyed)
            return;
        world.RunSynchronously(() =>
        {
            if (!IsDestroyed)
                action();
        });
    }

    private void Await<T>(Task<T> task, Action<T> then)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion)
                OnUi(() => then(t.Result));
            else
                OnUi(() =>
                {
                    _busy = false;
                    _loading = false;
                    SetStatus(t.Exception?.GetBaseException().Message ?? "That did not work.", DashTheme.Warning);
                });
        });
    }

    // THE BAR

    private void RebuildBar()
    {
        var bar = _bar;
        if (bar == null || bar.IsDestroyed)
            return;
        bar.DestroyChildren();
        _dropRects.Clear();

        bool signedIn = _source.SignedIn;
        bool carrying = _carried.HasValue;
        bool canUp = _chain.Count > 1;
        var up = AddInlineButton(bar, "Up", canUp ? TabFill : ControlFill, 56f, GoUp);
        if (canUp)
            _dropRects.Add((up.GetComponent<RectTransform>()!, _chain[_chain.Count - 2].id, _chain[_chain.Count - 2].name));

        var here = bar.AddSlot("Here");
        here.AttachComponent<RectTransform>();
        here.AttachComponent<LayoutElement>().FlexibleWidth.Value = 1f;
        var hereText = _parts.Label(here, BrowserParts.Truncate(FolderName, 40), DashTheme.FontBody, DashTheme.TextDim, BrowserParts.Weight.Semibold);
        hereText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        if (!signedIn)
        {
            MarkDirty();
            RebuildStatusRow();
            return;
        }

        if (carrying)
        {
            var carried = _carried!.Value;
            var hint = bar.AddSlot("Hint");
            hint.AttachComponent<RectTransform>();
            BrowserParts.Size(hint, 400f, DashTheme.ControlHeight);
            var hintText = _parts.Label(hint, $"Moving \"{BrowserParts.Truncate(carried.Name, 22)}\": press a folder, a step in the rail, or Up",
                DashTheme.FontSmall, DashTheme.TextDim, BrowserParts.Weight.Regular);
            hintText.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
            AddInlineButton(bar, "Cancel", TabFill, 84f, CancelCarry);
            RebuildStatusRow();
            MarkDirty();
            return;
        }

        // The right hand group changes with the selection: with nothing selected it is the two ways
        // in from the world, with something selected it is what can be done to that thing.
        var selected = SelectedEntry();
        if (selected.HasValue)
        {
            var entry = selected.Value;
            AddInlineButton(bar, "Rename", TabFill, 80f, () => OpenDialog(DialogKind.Rename));
            AddInlineButton(bar, _confirmDeleteLeft > 0f ? "Confirm" : "Delete",
                _confirmDeleteLeft > 0f ? DashTheme.Negative : TabFill, 76f, DeleteSelected);
            switch (entry.Kind)
            {
                case InventoryEntryKind.Folder:
                    AddInlineButton(bar, "Open", AccentColor, 76f, () => NavigateInto(entry));
                    break;
                case InventoryEntryKind.Item:
                    if (entry.IsAvatar)
                        AddInlineButton(bar, "Equip", TabFill, 70f, () => EquipItem(entry));
                    AddInlineButton(bar, "Spawn", AccentColor, 76f, () => SpawnItem(entry));
                    break;
                case InventoryEntryKind.World:
                    AddInlineButton(bar, "Orb", TabFill, 60f, () => SpawnWorldOrb(entry));
                    AddInlineButton(bar, "Open", AccentColor, 76f, () => OpenWorld(entry));
                    break;
            }
        }
        else if (_search.Length == 0)
        {
            AddInlineButton(bar, "Save held", TabFill, 96f, SaveHeld);
            AddInlineButton(bar, "Save avatar", TabFill, 108f, SaveAvatar);
        }

        BuildSearchWell(bar);
        AddInlineButton(bar, SortLabel(_sort), TabFill, 80f, CycleSort);
        AddInlineButton(bar, _pickForMove ? "Pick" : "Move", _pickForMove ? AccentColor : TabFill, 66f, StartMove);
        RebuildStatusRow();
        MarkDirty();
    }

    private void BuildSearchWell(Slot bar)
    {
        var cell = bar.AddSlot("SearchWell");
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = 180f;
        element.PreferredWidth.Value = 180f;
        element.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(cell, ControlFill, _focus == Field.Search ? AccentColor : RowBorder);
        var button = cell.AttachComponent<Button>();
        button.Clicked += (_, _) => { _focus = _focus == Field.Search ? Field.None : Field.Search; RebuildBar(); };
        bool empty = _search.Length == 0;
        var text = AddFillLabel(cell, empty ? (_focus == Field.Search ? "Type to search" : "Search") : _search,
            DashTheme.FontBody, empty ? TextDim : TextPrimary);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    // Left: the last message, or the selection's facts, or the listing. Right: the pages.
    private void RebuildStatusRow()
    {
        var row = _statusRow;
        if (row == null || row.IsDestroyed)
            return;
        row.DestroyChildren();

        var selected = SelectedEntry();
        string text = _notice ?? (_loading ? "Loading…" : selected.HasValue ? DescribeEntry(selected.Value) : DescribeListing());
        var tint = _notice != null ? _noticeColor : DashTheme.TextDim;
        _notice = null;

        var textSlot = row.AddSlot("Text");
        textSlot.AttachComponent<RectTransform>();
        textSlot.AttachComponent<LayoutElement>().FlexibleWidth.Value = 1f;
        _status = _parts.Label(textSlot, text, DashTheme.FontSmall, tint, BrowserParts.Weight.Regular);
        _status.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        int pages = PageCount();
        if (pages <= 1)
            return;
        AddInlineButton(row, "‹", _page > 0 ? TabFill : ControlFill, 40f, PrevPage);
        var pageSlot = row.AddSlot("Page");
        pageSlot.AttachComponent<RectTransform>();
        BrowserParts.Size(pageSlot, 72f, StatusHeight);
        _parts.Label(pageSlot, $"{_page + 1} / {pages}", DashTheme.FontSmall, DashTheme.TextDim, BrowserParts.Weight.Regular);
        AddInlineButton(row, "›", _page < pages - 1 ? TabFill : ControlFill, 40f, NextPage);
    }

    private void SetStatus(string text, in color tint)
    {
        _notice = text;
        _noticeColor = tint;
        if (_status != null && !_status.IsDestroyed)
        {
            BrowserParts.SetText(_status, text);
            BrowserParts.SetColor(_status.Color, tint);
        }
    }

    private static string DescribeEntry(in InventoryEntry entry)
    {
        if (entry.IsFolder)
            return $"{entry.Name} · folder · {(entry.ChildCount == 1 ? "1 thing inside" : $"{entry.ChildCount} things inside")}";
        string kind = entry.Kind == InventoryEntryKind.World ? "world" : entry.IsAvatar ? "avatar" : "item";
        return $"{entry.Name} · {kind} · {entry.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {Inventory.FormatBytes(entry.SizeBytes)}";
    }

    private string DescribeListing()
    {
        if (!_source.SignedIn)
            return "Sign in from the Home screen to use your inventory.";
        int folders = 0, worlds = 0, items = 0;
        foreach (var e in _entries)
        {
            if (e.IsFolder) folders++;
            else if (e.Kind == InventoryEntryKind.World) worlds++;
            else items++;
        }
        if (_search.Length > 0)
            return $"{_entries.Count} match \"{_search}\"";
        if (folders == 0 && worlds == 0 && items == 0)
            return _chain.Count == 1
                ? "Nothing saved yet. Save what you hold or wear, or a world from the Session tab."
                : "This folder is empty.";
        var parts = new List<string>(3);
        if (folders > 0) parts.Add(folders == 1 ? "1 folder" : $"{folders} folders");
        if (items > 0) parts.Add(items == 1 ? "1 item" : $"{items} items");
        if (worlds > 0) parts.Add(worlds == 1 ? "1 world" : $"{worlds} worlds");
        return string.Join(" · ", parts);
    }

    private static string SortLabel(Sort sort) => sort switch
    {
        Sort.AToZ => "A to Z",
        Sort.ZToA => "Z to A",
        Sort.Newest => "Newest",
        Sort.Oldest => "Oldest",
        _ => "By type",
    };

    private void CycleSort()
    {
        _sort = _sort switch
        {
            Sort.AToZ => Sort.ZToA,
            Sort.ZToA => Sort.Newest,
            Sort.Newest => Sort.Oldest,
            Sort.Oldest => Sort.ByType,
            _ => Sort.AToZ,
        };
        _page = 0;
        ApplyView();
    }

    private bool Passes(in InventoryEntry entry) => _show switch
    {
        Show.Worlds => entry.IsFolder || entry.Kind == InventoryEntryKind.World,
        Show.Items => entry.IsFolder || (entry.Kind == InventoryEntryKind.Item && !entry.IsAvatar),
        Show.Avatars => entry.IsFolder || entry.IsAvatar,
        _ => true,
    };

    // NAVIGATION

    private void NavigateToRoot()
    {
        _chain.Clear();
        _chain.Add((Inventory.Root, "Inventory"));
        AfterNavigate();
    }

    private void NavigateInto(InventoryEntry folder)
    {
        if (!folder.IsFolder)
            return;
        if (_carried.HasValue)
            CancelCarry();
        // A search result can sit anywhere in the tree; opening it lands there without the steps
        // between, which the rail shows as root then the folder.
        if (_search.Length > 0)
        {
            _chain.Clear();
            _chain.Add((Inventory.Root, "Inventory"));
        }
        _chain.Add((folder.Id, folder.Name));
        AfterNavigate();
    }

    private void NavigateBackTo(string folderId)
    {
        if (_carried.HasValue)
            CancelCarry();
        for (int i = 0; i < _chain.Count; i++)
        {
            if (_chain[i].id != folderId)
                continue;
            _chain.RemoveRange(i + 1, _chain.Count - i - 1);
            break;
        }
        AfterNavigate();
    }

    private void GoUp()
    {
        if (_chain.Count <= 1)
            return;
        if (_carried.HasValue)
            CancelCarry();
        _chain.RemoveAt(_chain.Count - 1);
        AfterNavigate();
    }

    private void AfterNavigate()
    {
        _selectedId = null;
        _pickForMove = false;
        _search = string.Empty;
        _focus = Field.None;
        _page = 0;
        _sidebar?.SetPath(_chain);
        Reload();
    }

    // LOADING

    private void Reload()
    {
        if (_contentSlot == null)
            return;
        int serial = ++_loadSerial;
        if (!_source.SignedIn)
        {
            _entries.Clear();
            _loading = false;
            ApplyView();
            return;
        }
        _loading = true;
        RebuildStatusRow();
        var task = _search.Length > 0 ? _source.Search(_search) : _source.List(FolderId);
        Await(task, listing =>
        {
            if (serial != _loadSerial)
                return;
            _loading = false;
            _entries.Clear();
            _entries.AddRange(listing.Entries);
            if (listing.Problem != null)
            {
                _notice = listing.Problem;
                _noticeColor = DashTheme.Warning;
            }
            ApplyView();
        });
    }

    // Filter, sort, page and draw what is loaded. Cheap enough to run on every sort or filter press.
    private void ApplyView()
    {
        _visible.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (Passes(_entries[i]))
                _visible.Add(_entries[i]);
        }
        _visible.Sort(CompareEntries);

        if (_selectedId != null && !_visible.Exists(e => e.Id == _selectedId))
            _selectedId = null;

        int worlds = 0, items = 0, avatars = 0;
        foreach (var e in _entries)
        {
            if (e.Kind == InventoryEntryKind.World) worlds++;
            else if (e.IsAvatar) avatars++;
            else if (e.Kind == InventoryEntryKind.Item) items++;
        }
        _sidebar?.SetCount((int)Show.All, _entries.Count);
        _sidebar?.SetCount((int)Show.Worlds, worlds);
        _sidebar?.SetCount((int)Show.Items, items);
        _sidebar?.SetCount((int)Show.Avatars, avatars);

        if (_page > PageCount() - 1) _page = PageCount() - 1;
        if (_page < 0) _page = 0;
        int rows = (PageLength + Columns - 1) / Columns;
        if (rows < 1) rows = 1;
        SetContentHeight(rows * InventoryCard.Height + (rows - 1) * CardSpacing + ContentPad * 2f);

        if (_scroll != null)
            _scroll.AbsolutePosition = float2.Zero;
        _boundFirstRow = -1;
        RelayoutVirtual(force: true);
        RebuildBar();
        _scrollbar?.Refresh();
        World?.RunInUpdates(2, () => _scrollbar?.Refresh());
    }

    // A hairline row. It has to be a row in the column, not a line pinned inside the thing above it:
    // a pinned child resolves against its parent's AUTHORED rect, and a parent whose rect comes from a
    // layout still carries the RectTransform default. -xlinka
    private static void Rule(Slot column, in color tint)
    {
        var rule = column.AddSlot("Rule");
        rule.AttachComponent<RectTransform>();
        BrowserParts.Height(rule, 1f);
        rule.AttachComponent<Image>().Tint.Value = tint;
    }

    private int CompareEntries(InventoryEntry a, InventoryEntry b)
    {
        if (a.IsFolder != b.IsFolder)
            return a.IsFolder ? -1 : 1;
        switch (_sort)
        {
            case Sort.Newest:
            {
                int byTime = b.ModifiedUtc.CompareTo(a.ModifiedUtc);
                if (byTime != 0) return byTime;
                break;
            }
            case Sort.Oldest:
            {
                int byTime = a.ModifiedUtc.CompareTo(b.ModifiedUtc);
                if (byTime != 0) return byTime;
                break;
            }
            case Sort.ByType:
            {
                int byKind = TypeRank(a).CompareTo(TypeRank(b));
                if (byKind != 0) return byKind;
                break;
            }
            case Sort.ZToA:
                return string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
        }
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int TypeRank(in InventoryEntry entry)
    {
        if (entry.IsFolder) return 0;
        if (entry.Kind == InventoryEntryKind.World) return 1;
        return entry.IsAvatar ? 2 : 3;
    }

    private int PageCount() => System.Math.Max(1, (_visible.Count + PageSize - 1) / PageSize);
    private int PageStart => _page * PageSize;
    private int PageLength => System.Math.Min(PageSize, System.Math.Max(0, _visible.Count - PageStart));

    private void PrevPage()
    {
        if (_page <= 0) return;
        _page--;
        _selectedId = null;
        ApplyView();
    }

    private void NextPage()
    {
        if (_page >= PageCount() - 1) return;
        _page++;
        _selectedId = null;
        ApplyView();
    }

    // THE GRID

    private void SetContentHeight(float height)
    {
        if (_contentRect != null)
            _contentRect.OffsetMin.Value = new float2(0f, -height);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        RelayoutVirtual(force: false);
        if (_confirmDeleteLeft > 0f)
        {
            _confirmDeleteLeft -= delta;
            if (_confirmDeleteLeft <= 0f)
            {
                _confirmDeleteLeft = 0f;
                RebuildBar();
            }
        }
        if (_artDirty)
        {
            _artDirty = false;
            for (int i = 0; i < _pool.Count; i++)
            {
                var card = _pool[i];
                if (!card.Active || card.Kind != InventoryEntryKind.World)
                    continue;
                var entry = EntryById(card.Id);
                if (entry.HasValue)
                    card.ApplyArt(ThumbnailFor(entry.Value));
            }
        }
    }

    private void RelayoutVirtual(bool force)
    {
        if (_contentSlot == null || _contentRect == null || _viewportRect == null || _scroll == null)
            return;
        float viewportH = _viewportRect.LocalComputeRect.height;
        float contentW = _contentRect.LocalComputeRect.width;
        if (viewportH <= LaidOutFloor || contentW <= 0f)
            return;

        float rowStride = InventoryCard.Height + CardSpacing;
        int visibleRows = (int)MathF.Ceiling(viewportH / rowStride) + BufferRows;
        int poolCount = System.Math.Max(1, visibleRows * Columns);
        float cardW = (contentW - 2f * ContentPad - (Columns - 1) * CardSpacing) / Columns;
        if (cardW <= 0f)
            return;
        EnsurePool(poolCount, cardW);

        if (MathF.Abs(cardW - _lastCardW) > 0.5f)
        {
            force = true;
            _lastCardW = cardW;
        }

        float scrollY = _scroll.AbsolutePosition.y;
        int firstRow = (int)MathF.Floor((scrollY - ContentPad) / rowStride);
        if (firstRow < 0) firstRow = 0;
        if (!force && firstRow == _boundFirstRow)
            return;
        if (force)
        {
            for (int i = 0; i < _pool.Count; i++)
                _pool[i].EntryIndex = -1;
        }
        _boundFirstRow = firstRow;

        int start = firstRow * Columns;
        int end = System.Math.Min(PageLength, (firstRow + visibleRows) * Columns);
        for (int e = start; e < end; e++)
        {
            var card = _pool[e % poolCount];
            if (card.EntryIndex == e && card.Active)
                continue;
            var entry = _visible[PageStart + e];
            int row = e / Columns;
            int colIndex = e % Columns;
            card.Place(ContentPad + colIndex * (cardW + CardSpacing), ContentPad + row * rowStride, cardW);
            card.Bind(in entry, e);
            card.ApplyArt(entry.Kind == InventoryEntryKind.World ? ThumbnailFor(entry) : null);
            PaintCard(card);
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            var card = _pool[i];
            if (card.Active && (card.EntryIndex < start || card.EntryIndex >= end))
                card.Hide();
        }
    }

    private void EnsurePool(int count, float cardW)
    {
        if (_contentSlot == null)
            return;
        while (_pool.Count < count)
        {
            var card = new InventoryCard(_parts, _contentSlot, cardW);
            card.Pressed = OnCardPressed;
            _pool.Add(card);
        }
    }

    private void PaintCard(InventoryCard card)
    {
        bool selected = _selectedId != null && card.Id == _selectedId;
        bool target = _dropTarget != null && card.Id == _dropTarget;
        bool carried = _carried.HasValue && card.Id == _carried.Value.Id;
        card.SetState(selected, target, carried);
    }

    private void RepaintCards()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Active)
                PaintCard(_pool[i]);
        }
    }

    // A world's picture comes down once per hash and lives in the thumbnail cache from then on.
    private IAssetProvider<TextureAsset>? ThumbnailFor(in InventoryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.ThumbnailHash))
            return null;
        string key = "thumb|" + entry.ThumbnailHash;
        var have = _thumbnails.Get(key);
        if (have != null)
            return have;
        if (_thumbnailsAsked.Add(entry.ThumbnailHash!))
        {
            var captured = entry;
            Await(_source.FetchThumbnail(captured), bytes =>
            {
                if (bytes == null || bytes.Length == 0)
                    return;
                _thumbnails.OfferBase64(key, Convert.ToBase64String(bytes), () => _artDirty = true);
            });
        }
        return null;
    }

    private InventoryEntry? SelectedEntry() => _selectedId == null ? null : EntryById(_selectedId);

    private InventoryEntry? EntryById(string id)
    {
        for (int i = 0; i < _visible.Count; i++)
        {
            if (_visible[i].Id == id)
                return _visible[i];
        }
        return null;
    }

    // One press selects. A second press on the same card within the double press window opens it:
    // a folder navigates, an item spawns, a world opens. Move's pick step takes the press instead.
    private void OnCardPressed(InventoryCard card, UIInteractionContext context)
    {
        if (_carried.HasValue || _busy)
            return;
        var entry = EntryById(card.Id);
        if (!entry.HasValue)
            return;
        _focus = Field.None;
        if (_pickForMove)
        {
            _pickForMove = false;
            BeginCarry(entry.Value, context);
            return;
        }

        var now = DateTime.UtcNow;
        bool second = _lastPressId != null && _lastPressId == card.Id && (now - _lastPress).TotalSeconds <= DoublePressSeconds;
        _lastPress = now;
        _lastPressId = card.Id;
        _confirmDeleteLeft = 0f;

        if (second)
        {
            _lastPressId = null;
            switch (entry.Value.Kind)
            {
                case InventoryEntryKind.Folder: NavigateInto(entry.Value); break;
                case InventoryEntryKind.Item: SpawnItem(entry.Value); break;
                case InventoryEntryKind.World: OpenWorld(entry.Value); break;
            }
            return;
        }

        _selectedId = card.Id;
        RepaintCards();
        RebuildBar();
    }

    // MOVE

    private void StartMove()
    {
        if (_carried.HasValue)
        {
            CancelCarry();
            return;
        }
        var selected = SelectedEntry();
        if (selected.HasValue)
        {
            BeginCarry(selected.Value, null);
            return;
        }
        _pickForMove = !_pickForMove;
        SetStatus(_pickForMove ? "Press the folder, item or world you want to move." : DescribeListing(), DashTheme.TextDim);
        RebuildBar();
    }

    private void BeginCarry(InventoryEntry entry, UIInteractionContext? context)
    {
        _carried = entry;
        _dropTarget = null;
        _selectedId = null;
        _confirmDeleteLeft = 0f;
        _catcher!.Slot.ActiveSelf.Value = true;
        _ghost!.Show(entry.Name, entry.Kind switch
        {
            InventoryEntryKind.Folder => "Folder",
            InventoryEntryKind.World => "World",
            _ => entry.IsAvatar ? "Avatar" : "Item",
        });
        if (context.HasValue)
            _ghost.MoveTo(context.Value.LocalPoint);
        SetStatus("Escape puts it back.", DashTheme.TextDim);
        RepaintCards();
        RebuildBar();
    }

    private void CancelCarry()
    {
        if (!_carried.HasValue)
            return;
        _carried = null;
        _dropTarget = null;
        _pickForMove = false;
        _catcher!.Slot.ActiveSelf.Value = false;
        _ghost!.Hide();
        SetStatus(DescribeListing(), DashTheme.TextDim);
        RepaintCards();
        RebuildBar();
    }

    private void OnCarryMoved(UIInteractionContext context)
    {
        if (!_carried.HasValue || _ghost == null)
            return;
        _ghost.MoveTo(context.LocalPoint);
        string? target = FolderCardAt(context.LocalPoint)?.Id;
        if (!string.Equals(target, _dropTarget, StringComparison.Ordinal))
        {
            _dropTarget = target;
            RepaintCards();
        }
    }

    private void OnCarryDropped(UIInteractionContext context)
    {
        if (!_carried.HasValue)
            return;
        var carried = _carried.Value;
        string? targetId = null;
        string targetName = "there";
        var card = FolderCardAt(context.LocalPoint);
        if (card != null)
        {
            var folder = EntryById(card.Id);
            targetId = card.Id;
            targetName = folder?.Name ?? "that folder";
        }
        else
        {
            foreach (var (rect, id, name) in _dropRects)
            {
                if (rect != null && !rect.IsDestroyed && rect.LocalComputeRect.Contains(context.LocalPoint))
                {
                    targetId = id;
                    targetName = name;
                    break;
                }
            }
            if (targetId == null && _sidebar != null)
            {
                foreach (var (rect, id) in _sidebar.TreeRects)
                {
                    if (rect != null && !rect.IsDestroyed && rect.LocalComputeRect.Contains(context.LocalPoint))
                    {
                        targetId = id;
                        for (int i = 0; i < _chain.Count; i++)
                            if (_chain[i].id == id) targetName = _chain[i].name;
                        break;
                    }
                }
            }
        }

        _carried = null;
        _dropTarget = null;
        _catcher!.Slot.ActiveSelf.Value = false;
        _ghost!.Hide();
        if (targetId == null)
        {
            SetStatus(DescribeListing(), DashTheme.TextDim);
            RepaintCards();
            RebuildBar();
            return;
        }
        RunAction(_source.Move(carried, targetId, targetName));
    }

    private InventoryCard? FolderCardAt(in float2 point)
    {
        var canvas = _dashboard?.Slot.GetComponent<Canvas>();
        if (canvas == null || _viewportRect == null || !_viewportRect.LocalComputeRect.Contains(point))
            return null;
        for (int i = 0; i < _pool.Count; i++)
        {
            var card = _pool[i];
            if (!card.Active || card.Kind != InventoryEntryKind.Folder)
                continue;
            if (_carried.HasValue && card.Id == _carried.Value.Id)
                continue;
            if (card.Contains(point, canvas.ScrollOffsetOf(card.Root)))
                return card;
        }
        return null;
    }

    // ACTIONS

    // A change on the cloud: the answer goes on the status line and the folder is read again.
    private void RunAction(Task<InventoryResult> task)
    {
        if (_busy)
            return;
        _busy = true;
        SetStatus("Working…", DashTheme.TextDim);
        Await(task, result =>
        {
            _busy = false;
            _notice = result.Message;
            _noticeColor = result.Ok ? DashTheme.TextDim : DashTheme.Warning;
            if (result.Ok)
            {
                _selectedId = null;
                Reload();
            }
            else
            {
                RebuildBar();
            }
        });
    }

    private void DeleteSelected()
    {
        var selected = SelectedEntry();
        if (!selected.HasValue)
            return;
        if (_confirmDeleteLeft <= 0f)
        {
            _confirmDeleteLeft = ConfirmSeconds;
            SetStatus(selected.Value.IsFolder
                ? $"Press Confirm to delete \"{selected.Value.Name}\" and everything in it."
                : $"Press Confirm to delete \"{selected.Value.Name}\".", DashTheme.Warning);
            RebuildBar();
            return;
        }
        _confirmDeleteLeft = 0f;
        RunAction(_source.Delete(selected.Value));
    }

    private World? FocusedWorld => Lumora.Core.Engine.Current?.WorldManager?.FocusedWorld;

    private bool CanSpawnHere(World world, out string? refusal)
    {
        refusal = null;
        // Bringing your own item in is the Spawn domain, same question the in-world dispensers ask.
        // The world's own mode ceiling is inside that answer, so an event world refuses here too.
        if (!world.AllowsItemSpawning
            || world.DataModelPermissions?.AllowsDomain(world.LocalUser, DataModelPermissionDomain.Spawn) == false)
        {
            refusal = "This world does not let you spawn items.";
            return false;
        }
        return true;
    }

    private float3 SpawnPosition(World world)
    {
        var userRoot = world.LocalUser?.Root;
        return userRoot?.HeadSlot != null
            ? userRoot.HeadPosition + userRoot.HeadRotation * (float3.Backward * 1.0f)
            : new float3(0f, 1f, 0f);
    }

    // Fetch first, on a task; load on the world thread once the file is here.
    private void SpawnItem(InventoryEntry entry) => FetchThen(entry, (world, path) =>
    {
        if (!CanSpawnHere(world, out var refusal))
        {
            SetStatus(refusal!, DashTheme.Warning);
            return;
        }
        var spawned = Inventory.LoadItem(world, path, entry.Name);
        if (spawned == null)
        {
            SetStatus($"Couldn't load \"{entry.Name}\".", DashTheme.Warning);
            return;
        }
        spawned.GlobalPosition = SpawnPosition(world);
        InspectorUndo.Record(world, SlotExistenceUndoBatch.Created(world, new[] { spawned }, UndoLocale.SpawnItem));
        SetStatus($"\"{entry.Name}\" is in front of you.", DashTheme.TextDim);
    });

    private void EquipItem(InventoryEntry entry) => FetchThen(entry, (world, path) =>
    {
        var manager = world.LocalUser?.Root?.GetRegisteredComponent<Lumora.Core.Components.Avatar.AvatarEquipManager>();
        if (manager == null)
        {
            SetStatus("There is nothing to wear it on here.", DashTheme.Warning);
            return;
        }
        if (!CanSpawnHere(world, out var refusal))
        {
            SetStatus(refusal!, DashTheme.Warning);
            return;
        }
        var spawned = Inventory.LoadItem(world, path, entry.Name);
        if (spawned == null)
        {
            SetStatus($"Couldn't load \"{entry.Name}\".", DashTheme.Warning);
            return;
        }
        spawned.GlobalPosition = SpawnPosition(world);
        InspectorUndo.Record(world, SlotExistenceUndoBatch.Created(world, new[] { spawned }, UndoLocale.SpawnItem));
        SetStatus(manager.EquipAvatar(spawned) ? $"Wearing \"{entry.Name}\"." : $"\"{entry.Name}\" is in front of you, but it would not equip.",
            DashTheme.TextDim);
    });

    private void OpenWorld(InventoryEntry entry) => FetchThen(entry, (_, path) =>
    {
        var manager = Lumora.Core.Engine.Current?.WorldManager;
        if (manager == null)
        {
            SetStatus("The world manager isn't up yet.", DashTheme.Warning);
            return;
        }
        var world = manager.OpenSavedWorld(path, entry.Name);
        SetStatus(world != null ? $"Opened \"{entry.Name}\"." : $"Couldn't open \"{entry.Name}\".", world != null ? DashTheme.TextDim : DashTheme.Warning);
    });

    // The orb points at the fetched file on this machine, so it opens here and refuses anywhere else.
    private void SpawnWorldOrb(InventoryEntry entry) => FetchThen(entry, (world, path) =>
    {
        bool queued = WorldOrb.SpawnForSavedWorld(world, path, out var reason);
        SetStatus(queued ? $"Orb for \"{entry.Name}\" is in front of you." : $"Couldn't place that orb: {reason ?? "refused"}.",
            queued ? DashTheme.TextDim : DashTheme.Warning);
    });

    private void FetchThen(InventoryEntry entry, Action<World, string> then)
    {
        if (_busy)
            return;
        var world = FocusedWorld;
        if (world == null)
        {
            SetStatus("The world manager isn't up yet.", DashTheme.Warning);
            return;
        }
        _busy = true;
        SetStatus($"Fetching \"{entry.Name}\"…", DashTheme.TextDim);
        Await(_source.FetchToCache(entry), fetched =>
        {
            _busy = false;
            if (fetched.path == null)
            {
                SetStatus(fetched.problem ?? "The download did not go through.", DashTheme.Warning);
                return;
            }
            var target = FocusedWorld ?? world;
            if (target.IsDestroyed)
            {
                SetStatus("That world is gone.", DashTheme.Warning);
                return;
            }
            then(target, fetched.path);
        });
    }

    // Everything in either hand that may be copied out, saved into the folder being looked at.
    private void SaveHeld()
    {
        if (_busy)
            return;
        var world = FocusedWorld;
        var root = world?.LocalUser?.Root;
        if (world == null || root == null)
        {
            SetStatus("The world manager isn't up yet.", DashTheme.Warning);
            return;
        }
        var slots = new List<Slot>();
        foreach (var grabber in root.Slot.GetComponentsInChildren<Lumora.Core.Components.Interaction.Grabber>())
        {
            foreach (var grabbable in grabber.GrabbedObjects)
            {
                var slot = (grabbable as Component)?.Slot;
                if (slot != null && !slot.IsDestroyed)
                    slots.Add(slot);
            }
        }
        if (slots.Count == 0)
        {
            SetStatus("Nothing in your hands. Grab something first.", DashTheme.TextDim);
            return;
        }
        var uploads = new List<Task<InventoryResult>>();
        int refused = 0;
        string? lastRefusal = null;
        foreach (var slot in slots)
        {
            if (Lumora.Core.Components.Interaction.GrabSaveBlock.BlocksInventorySave(slot))
            {
                refused++;
                lastRefusal = "marked not saveable";
                continue;
            }
            if (!Components.ItemProtection.AllowsSaveCopy(slot, world.LocalUser, out var reason))
            {
                refused++;
                lastRefusal = reason ?? "not allowed";
                continue;
            }
            uploads.Add(_source.SaveItem(slot, slot.Name, FolderId));
        }
        if (uploads.Count == 0)
        {
            SetStatus($"Nothing could be saved: {lastRefusal}.", DashTheme.Warning);
            return;
        }
        _busy = true;
        SetStatus(uploads.Count == 1 ? "Uploading 1 item…" : $"Uploading {uploads.Count} items…", DashTheme.TextDim);
        int refusedCount = refused;
        string? refusalText = lastRefusal;
        Await(Task.WhenAll(uploads), results =>
        {
            _busy = false;
            int saved = 0;
            string? failure = null;
            foreach (var r in results)
            {
                if (r.Ok) saved++; else failure = r.Message;
            }
            string line = saved == 1 ? "Saved 1 item here." : $"Saved {saved} items here.";
            if (failure != null)
                line += $" One failed: {failure}";
            if (refusedCount > 0)
                line += refusedCount == 1 ? $" One was refused: {refusalText}." : $" {refusedCount} were refused: {refusalText}.";
            _notice = line;
            _noticeColor = saved == 0 ? DashTheme.Warning : DashTheme.TextDim;
            Reload();
        });
    }

    // The avatar being worn, saved as it is. The socket it hangs on lives outside the avatar's own
    // tree, so the upload carries no link to it and the copy comes back unworn.
    private void SaveAvatar()
    {
        if (_busy)
            return;
        var world = FocusedWorld;
        var manager = world?.LocalUser?.Root?.GetRegisteredComponent<Lumora.Core.Components.Avatar.AvatarEquipManager>();
        var avatar = manager?.CurrentAvatar.Target;
        if (world == null || avatar == null || avatar.IsDestroyed)
        {
            SetStatus("You are not wearing an avatar.", DashTheme.Warning);
            return;
        }
        if (!Components.ItemProtection.AllowsSaveCopy(avatar, world.LocalUser, out var reason))
        {
            SetStatus($"That avatar can't be saved: {reason ?? "not allowed"}.", DashTheme.Warning);
            return;
        }
        _busy = true;
        SetStatus("Uploading your avatar…", DashTheme.TextDim);
        Await(_source.SaveItem(avatar, avatar.Name, FolderId), result =>
        {
            _busy = false;
            _notice = result.Message;
            _noticeColor = result.Ok ? DashTheme.TextDim : DashTheme.Warning;
            Reload();
        });
    }

    // DIALOG: one text field, used for a new folder's name and for renaming.

    private void BuildDialog(Slot root)
    {
        _dialogRoot = root.AddSlot("Dialog");
        BrowserParts.Fill(_dialogRoot.AttachComponent<RectTransform>());
        _dialogRoot.AttachComponent<IgnoreLayout>();
        _dialogRoot.ActiveSelf.Value = false;

        var backdrop = _dialogRoot.AddSlot("Backdrop");
        BrowserParts.Fill(backdrop.AttachComponent<RectTransform>());
        backdrop.AttachComponent<Image>().Tint.Value = new color(0f, 0f, 0f, 0.55f);
        backdrop.AttachComponent<Button>().Clicked += (_, _) => CloseDialog();

        var panel = _dialogRoot.AddSlot("Panel");
        var panelRect = panel.AttachComponent<RectTransform>();
        panelRect.AnchorMin.Value = new float2(0.5f, 0.5f);
        panelRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        panelRect.OffsetMin.Value = new float2(-220f, -92f);
        panelRect.OffsetMax.Value = new float2(220f, 92f);
        panel.AttachComponent<GraphicChunkRoot>();
        BrowserParts.Panel(panel, DashTheme.Panel, DashTheme.RadiusCard, DashTheme.Outline, 1.5f);
        panel.AttachComponent<Button>();

        var title = panel.AddSlot("Title");
        BrowserParts.PinTop(title.AttachComponent<RectTransform>(), 16f, 26f, 20f);
        _dialogTitle = _parts.Label(title, string.Empty, DashTheme.FontHeading, DashTheme.Text, BrowserParts.Weight.Bold);
        _dialogTitle.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        var well = panel.AddSlot("Well");
        BrowserParts.PinTop(well.AttachComponent<RectTransform>(), 54f, DashTheme.ControlHeight, 20f);
        BrowserParts.Panel(well, DashTheme.Field, DashTheme.RadiusControl, DashTheme.Accent, 1.5f);
        well.AttachComponent<Button>().Clicked += (_, _) => _focus = Field.Dialog;
        var wellText = well.AddSlot("Text");
        var wellTextRect = wellText.AttachComponent<RectTransform>();
        BrowserParts.Fill(wellTextRect);
        wellTextRect.OffsetMin.Value = new float2(12f, 0f);
        wellTextRect.OffsetMax.Value = new float2(-12f, 0f);
        _dialogText = _parts.Label(wellText, string.Empty, DashTheme.FontBody, DashTheme.Text, BrowserParts.Weight.Regular);
        _dialogText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        var problem = panel.AddSlot("Problem");
        BrowserParts.PinTop(problem.AttachComponent<RectTransform>(), 96f, 20f, 20f);
        _dialogError = _parts.Label(problem, string.Empty, DashTheme.FontSmall, DashTheme.Warning, BrowserParts.Weight.Regular);
        _dialogError.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        var buttons = panel.AddSlot("Buttons");
        BrowserParts.PinBottom(buttons.AttachComponent<RectTransform>(), 14f, DashTheme.ControlHeight, 20f);
        var buttonRow = buttons.AttachComponent<HorizontalLayout>();
        buttonRow.Spacing.Value = 8f;
        buttonRow.ForceExpandWidth.Value = false;
        buttonRow.ForceExpandHeight.Value = true;
        var fill = buttons.AddSlot("Fill");
        fill.AttachComponent<RectTransform>();
        fill.AttachComponent<LayoutElement>().FlexibleWidth.Value = 1f;
        AddInlineButton(buttons, "Cancel", TabFill, 84f, CloseDialog);
        AddInlineButton(buttons, "OK", AccentColor, 84f, ConfirmDialog);
    }

    private void OpenDialog(DialogKind kind)
    {
        if (_dialogRoot == null || !_source.SignedIn)
            return;
        if (_carried.HasValue)
            CancelCarry();
        var selected = SelectedEntry();
        if (kind == DialogKind.Rename && !selected.HasValue)
            return;
        _dialog = kind;
        _dialogBuffer = kind == DialogKind.Rename ? selected!.Value.Name : string.Empty;
        _dialogProblem = null;
        _focus = Field.Dialog;
        BrowserParts.SetText(_dialogTitle, kind == DialogKind.Rename
            ? $"Rename \"{BrowserParts.Truncate(selected!.Value.Name, 24)}\""
            : $"New folder in {BrowserParts.Truncate(FolderName, 22)}");
        UpdateDialog();
        _dialogRoot.ActiveSelf.Value = true;
        MarkDirty();
    }

    private void CloseDialog()
    {
        if (_dialogRoot == null || _dialog == DialogKind.None)
            return;
        _dialog = DialogKind.None;
        _focus = Field.None;
        _dialogRoot.ActiveSelf.Value = false;
        MarkDirty();
    }

    private void UpdateDialog()
    {
        bool empty = _dialogBuffer.Length == 0;
        BrowserParts.SetText(_dialogText, empty ? "Name" : _dialogBuffer);
        if (_dialogText != null)
            BrowserParts.SetColor(_dialogText.Color, empty ? DashTheme.TextDim : DashTheme.Text);
        BrowserParts.SetText(_dialogError, _dialogProblem ?? string.Empty);
        MarkDirty();
    }

    private void ConfirmDialog()
    {
        if (_dialog == DialogKind.None || _busy)
            return;
        string name = _dialogBuffer.Trim();
        if (!Inventory.ValidName(name, out var error))
        {
            _dialogProblem = error;
            UpdateDialog();
            return;
        }
        Task<InventoryResult> task;
        if (_dialog == DialogKind.NewFolder)
        {
            task = _source.CreateFolder(FolderId, name);
        }
        else
        {
            var selected = SelectedEntry();
            if (!selected.HasValue)
            {
                CloseDialog();
                return;
            }
            task = _source.Rename(selected.Value, name);
        }
        _busy = true;
        BrowserParts.SetText(_dialogError, "Working…");
        Await(task, result =>
        {
            _busy = false;
            if (!result.Ok)
            {
                _dialogProblem = result.Message;
                UpdateDialog();
                return;
            }
            CloseDialog();
            _notice = result.Message;
            _noticeColor = DashTheme.TextDim;
            _selectedId = null;
            Reload();
        });
    }

    // KEYS: the search well and the dialog's field share one route; whichever has focus takes them.

    public bool ConsumeChar(char c)
    {
        if (_focus == Field.Dialog && _dialog != DialogKind.None)
        {
            if (char.IsControl(c) || _dialogBuffer.Length >= 64)
                return true;
            _dialogBuffer += c;
            _dialogProblem = null;
            UpdateDialog();
            return true;
        }
        if (_focus == Field.Search)
        {
            if (char.IsControl(c) || _search.Length >= 48)
                return true;
            _search += c;
            _selectedId = null;
            _page = 0;
            Reload();
            return true;
        }
        return false;
    }

    public bool ConsumeBackspace()
    {
        if (_focus == Field.Dialog && _dialog != DialogKind.None)
        {
            if (_dialogBuffer.Length > 0)
            {
                _dialogBuffer = _dialogBuffer.Substring(0, _dialogBuffer.Length - 1);
                UpdateDialog();
            }
            return true;
        }
        if (_focus == Field.Search)
        {
            if (_search.Length > 0)
            {
                _search = _search.Substring(0, _search.Length - 1);
                Reload();
            }
            return true;
        }
        return false;
    }

    public bool ConsumeEnter()
    {
        if (_dialog != DialogKind.None)
        {
            ConfirmDialog();
            return true;
        }
        if (_focus == Field.Search)
        {
            _focus = Field.None;
            RebuildBar();
            return true;
        }
        return false;
    }

    public bool ConsumeEscape()
    {
        if (_dialog != DialogKind.None)
        {
            CloseDialog();
            return true;
        }
        if (_carried.HasValue || _pickForMove)
        {
            _pickForMove = false;
            CancelCarry();
            RebuildBar();
            return true;
        }
        if (_focus == Field.Search || _search.Length > 0)
        {
            _focus = Field.None;
            _search = string.Empty;
            Reload();
            return true;
        }
        if (_selectedId != null)
        {
            _selectedId = null;
            RepaintCards();
            RebuildBar();
            return true;
        }
        return false;
    }
}
