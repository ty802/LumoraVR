// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// receives inspector row-button actions routed through InspectorButtonRelay
public interface IInspectorActionHandler
{
    void HandleInspectorAction(string argument);
}

// handlers that also want the pressing user's interaction context - destructive actions use
// it to aim a confirmation at THAT user's radial menu before running
public interface IInspectorActionContextHandler
{
    void HandleInspectorAction(string argument, UIInteractionContext context);
}

// two panes: left is the slot hierarchy, right is the selected slot's members + one section per
// component. Tree is INCREMENTAL - one row object per visible slot, each owning its own child
// container - so expand/collapse/rename/reorder/live add-remove touch only the rows involved.
// Authority builds the replicated UI; selection/expansion synced so all peers agree. Local-only
// slots and [HideInInspector] components never appear.
[ComponentCategory("Utility/Inspectors")]
public class SceneInspectorPanel : Component, IInspectorActionHandler, IInspectorActionContextHandler
{
    public readonly SyncRef<Slot> Root;
    public readonly SyncRef<Slot> Selected;
    public readonly SyncFieldList<ulong> ExpandedSlots;
    public readonly SyncFieldList<ulong> ExpandedComponents;

    private readonly SyncRef<Slot> _hierarchyContent;
    private readonly SyncRef<Slot> _componentsContent;
    private readonly SyncRef<Text> _selectedTitle;
    private readonly SyncRef<Text> _rootTitle;
    private readonly SyncRef<ComponentSelectorPanel> _selector;

    private bool _hierarchyDirty = true;
    private bool _componentsDirty = true;

    // Visible-row cap, spent as an EXPANSION budget: the expansion that would cross it stops and
    // leaves a "+N more" marker in THAT container, instead of truncating the tree somewhere the user
    // never looked.
    private const int RowBudget = 250;
    private const float RowHeight = 30f;
    private const float IndentPerDepth = 18f;
    // Gap between rows. The scroll list, the node wrapper and every child container all use it, so a
    // nested row sits at exactly the pitch the flat list had.
    private const float TreeSpacing = 2f;

    // THE tree. One HierarchyRow per visible slot, keyed by slot RefID, owning that slot's row UI,
    // its child container and its live subscriptions. Every tree event resolves through this map to
    // ONE row and touches only that row's container, so expand, collapse, rename, reorder and live
    // add/remove never walk the tree, let alone rebuild it. Rows are their own graphics chunks, so an
    // untouched row keeps its baked mesh. -xlinka
    private readonly Dictionary<ulong, HierarchyRow> _rows = new();
    private int _visibleRows;
    private ulong _highlightedId;

    // Rows whose child set moved, drained once per OnChanges: a burst (an imported object dropping
    // fifty slots at once) costs one pass over the affected containers instead of fifty.
    private readonly HashSet<ulong> _pendingChildSync = new();
    private readonly List<HierarchyRow> _syncScratch = new();
    private bool _expansionDirty;
    private bool _traceDirty;

    // Live rename of the pane titles. Rows carry their own name watch and drop it with the row.
    private Sync<string>? _rootNameField;
    private Action<string>? _rootNameHandler;
    private Sync<string>? _titleNameField;
    private Action<string>? _titleNameHandler;

    private sealed class HierarchyRow
    {
        public Slot Target = null!;
        public ulong TargetId;
        public int Depth;
        public HierarchyRow? Parent;
        public readonly List<HierarchyRow> Children = new();

        // Node wraps the row and, while expanded, the container its children live in.
        public Slot NodeSlot = null!;
        public Slot RowSlot = null!;
        public Slot? ChildContainer;
        public Slot? OverflowRow;
        public int OverflowCount;

        public Text? Label;
        public Text? ExpanderLabel;
        public Image? Background;
        public color BaseColor;
        public bool EffectivePersistent;
        public bool Expanded;

        public Action<Slot, Slot>? ChildAdded;
        public Action<Slot, Slot>? ChildRemoved;
        public Action<Slot>? OrderChanged;
        public Action<Slot>? ActiveChanged;
        public Action<Slot>? PersistentChanged;
        public Action<string>? NameChanged;
    }

    public SceneInspectorPanel()
    {
        Root = new SyncRef<Slot>(this);
        Selected = new SyncRef<Slot>(this);
        ExpandedSlots = new SyncFieldList<ulong>(this);
        ExpandedComponents = new SyncFieldList<ulong>(this);
        _hierarchyContent = new SyncRef<Slot>(this);
        _componentsContent = new SyncRef<Slot>(this);
        _selectedTitle = new SyncRef<Text>(this);
        _rootTitle = new SyncRef<Text>(this);
        _selector = new SyncRef<ComponentSelectorPanel>(this);
    }

    public static SceneInspectorPanel Spawn(World world, Slot rootTarget)
    {
        var head = world.LocalUser?.Root?.HeadSlot;
        var panelSlot = world.RootSlot.AddSlot("Inspector");
        panelSlot.Persistent.Value = false;
        panelSlot.Tag.Value = "Developer";

        if (head != null)
        {
            float3 forward = head.GlobalRotation * float3.Backward; // view forward is -Z
            forward.y = 0f;
            forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;
            // Drop the panel centre below eye level so the HEADER sits near the gaze line and the body falls into the
            // natural downward view, instead of centring on the eyes (which makes you look UP to read the title). -xlinka
            panelSlot.GlobalPosition = head.GlobalPosition + forward * 1.0f + float3.Down * 0.12f;
            FaceHead(panelSlot, head);
        }
        panelSlot.LocalScale.Value = float3.One * 0.0005f;

        var panel = panelSlot.AttachComponent<SceneInspectorPanel>();
        panel.Root.Target = rootTarget;
        panel.Selected.Target = rootTarget;
        return panel;
    }

    // Yaw the panel so its readable +Z front points AT the head. Mirrors FaceLocalUser: the vector runs
    // FROM the panel TO the head, so a panel placed in front of the user turns to face back at them. The
    // old spawn yawed off the head's forward (pointing away), which showed the panel's back. -xlinka
    private static void FaceHead(Slot panelSlot, Slot head)
    {
        var toViewer = head.GlobalPosition - panelSlot.GlobalPosition;
        toViewer.y = 0f;
        if (toViewer.LengthSquared <= 1e-6f)
            return;
        panelSlot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));
    }

    // reference rows use this to open their target without losing the current view
    public static SceneInspectorPanel SpawnAdjacent(SceneInspectorPanel origin, Slot rootTarget)
    {
        var panelSlot = origin.Slot.Parent?.AddSlot("Inspector") ?? origin.World.RootSlot.AddSlot("Inspector");
        panelSlot.Persistent.Value = false;
        panelSlot.Tag.Value = "Developer";
        panelSlot.GlobalPosition = origin.Slot.GlobalPosition + origin.Slot.GlobalRotation * new float3(0.72f, 0f, 0f);
        panelSlot.GlobalRotation = origin.Slot.GlobalRotation;
        panelSlot.LocalScale.Value = origin.Slot.LocalScale.Value;

        var panel = panelSlot.AttachComponent<SceneInspectorPanel>();
        panel.Root.Target = rootTarget;
        panel.Selected.Target = rootTarget;
        return panel;
    }

    public override void OnAttach()
    {
        base.OnAttach();

        var theme = Slot.GetOrAttachComponent<UITheme>();
        // Inspector-specific dark set over the shared theme: near-black opaque panes so dense
        // editor rows read, with the house purple as the only accent. - xlinka
        theme.PanelBackground.Value = new color(0.075f, 0.07f, 0.115f, 1f);
        theme.Header.Value = new color(0.11f, 0.10f, 0.17f, 1f);
        theme.ButtonFill.Value = new color(0.22f, 0.20f, 0.34f, 1f);
        theme.Accent.Value = InspectorUI.AccentColor;
        theme.Separator.Value = new color(0.52f, 0.46f, 0.82f, 0.6f);
        theme.Border.Value = new color(0.52f, 0.46f, 0.82f, 0.45f);

        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Inspector";
        // Tall, narrow portrait tool: longer so more rows show at once, thinner so it
        // doesn't hog the view. Content still scrolls on overflow (bounded viewport), so height isn't the lever. -xlinka
        shell.Size.Value = new float2(1000f, 1360f);
        theme.ApplyTo(shell);

        shell.RebuildContent(BuildLayout);
        _hierarchyDirty = true;
        _componentsDirty = true;
    }

    private World? _subscribedWorld;

    public override void OnStart()
    {
        base.OnStart();
        // Live tree: react to slots spawned/removed in the world so the hierarchy stays current without a
        // manual reselect. Rows carry their own child watches, so these two are the backstop for add
        // paths that don't fire them; both feed the same per-row queue, which coalesces a burst (a whole
        // imported object, dozens of slots at once) into one reconcile pass next OnChanges. -xlinka
        _subscribedWorld = World;
        if (_subscribedWorld != null)
        {
            _subscribedWorld.OnSlotAdded += OnWorldSlotAdded;
            _subscribedWorld.OnSlotRemoved += OnWorldSlotRemoved;
        }
        // ExpandedSlots stays the synced source of truth, so watching the LIST catches a toggle from
        // any peer as well as our own button, and the reconcile touches only the rows that disagree.
        ExpandedSlots.OnChanged += OnExpandedSlotsChanged;
    }

    private void OnExpandedSlotsChanged(SyncFieldList<ulong> list)
    {
        _expansionDirty = true;
        MarkChangeDirty();
    }

    private void OnWorldSlotAdded(Slot slot)
    {
        // CRITICAL: ignore UI chrome. Building a row spawns slots (Node/Row/Button/Text); each fires this
        // event, and every inspector/panel is UI too - so a second inspector opened from a reference card
        // makes the two panels feed each other rows forever (hard lockup). We only care about SCENE slots,
        // so skip anything under a Canvas (all Helio UI roots at one), plus local render plumbing and
        // anything outside the displayed root. -xlinka
        if (slot == null || IsUiChrome(slot) || slot.IsLocalElement || !IsUnderDisplayedRoot(slot))
            return;
        // Backstop for any add path that doesn't fire the parent's own child event: queue the
        // PARENT's row. A slot under a collapsed (or absent) row resolves to no row and costs nothing.
        QueueChildSync(slot.Parent?.ReferenceID.RawValue ?? 0);
    }

    private void OnWorldSlotRemoved(Slot slot)
    {
        // Same filters as OnWorldSlotAdded: local render plumbing and slots outside the displayed root
        // never appear in the tree, so their destruction must not re-tessellate it (interaction flows
        // create/destroy local slots constantly while a panel is open).
        if (slot == null || IsUiChrome(slot) || slot.IsLocalElement || !IsUnderDisplayedRoot(slot))
            return;

        // The displayed ROOT died: the whole panel is a husk pointing at nothing - close it. Target
        // reads through RawTarget because a destroyed ref's Target already reads null here. Deferred:
        // this fires mid slot-removal and destroying the panel inline would re-enter the event.
        var root = Root.RawTarget;
        if (root != null && (ReferenceEquals(root, slot) || root.IsDestroyed))
        {
            World?.RunSynchronously(() => { if (!IsDestroyed) Slot.Destroy(); });
            return;
        }

        // A destroyed selection would otherwise leave a dead detail pane pointing at nothing; fall back
        // to the displayed root.
        var selected = Selected.Target;
        if (selected != null && (selected == slot || selected.IsDescendantOf(slot)))
        {
            Selected.Target = Root.Target;
            _componentsDirty = true;
        }
        // Backstop, same as the add side. The slot is off its parent's child list by now, so the
        // parent's reconcile drops that one row; a slot with no row queues nothing.
        var parentId = slot.Parent?.ReferenceID.RawValue ?? 0;
        if (parentId == 0 && _rows.TryGetValue(slot.ReferenceID.RawValue, out var orphan))
            parentId = orphan.Parent?.TargetId ?? 0;
        QueueChildSync(parentId);
    }

    // LIVE detail pane: watch the selected slot's component list so add/remove from ANY source (another
    // user, code, a tool) rebuilds the pane - not just this panel's own buttons. The events fire on the
    // WATCHED scene slot only, so this can't re-enter from the panel building its own UI rows.
    private Slot? _componentWatchSlot;

    private void UpdateComponentSubscription()
    {
        var selected = Selected.Target;
        if (ReferenceEquals(selected, _componentWatchSlot))
            return;
        if (_componentWatchSlot != null)
        {
            _componentWatchSlot.OnComponentAdded -= OnSelectedComponentsChanged;
            _componentWatchSlot.OnComponentRemoved -= OnSelectedComponentsChanged;
        }
        _componentWatchSlot = selected;
        if (selected != null)
        {
            selected.OnComponentAdded += OnSelectedComponentsChanged;
            selected.OnComponentRemoved += OnSelectedComponentsChanged;
        }
    }

    private void OnSelectedComponentsChanged(Slot slot, Component component)
    {
        _componentsDirty = true;
        MarkChangeDirty();
    }

    // Any Helio UI panel (this inspector, another inspector, a material panel, a reference card, the
    // dashboard, a menu) roots at a Canvas; scene content does not. A slot under a Canvas is chrome, not
    // scene structure, so it must never drive a tree rebuild. -xlinka
    private static bool IsUiChrome(Slot slot)
    {
        for (var s = slot; s != null; s = s.Parent)
        {
            if (s.GetComponent<Canvas>() != null)
                return true;
        }
        return false;
    }

    private bool IsUnderDisplayedRoot(Slot slot)
    {
        var root = Root.Target;
        if (root == null)
            return false;
        for (var s = slot; s != null; s = s.Parent)
        {
            if (s == root)
                return true;
        }
        return false;
    }

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        InspectorUI.ApplyTheme(ui, Slot);

        var vLayout = page.AttachComponent<VerticalLayout>();
        vLayout.Spacing.Value = 8f;
        vLayout.PaddingLeft.Value = 8f;
        vLayout.PaddingRight.Value = 8f;
        vLayout.PaddingTop.Value = 8f;
        vLayout.PaddingBottom.Value = 8f;
        vLayout.ForceExpandWidth.Value = true;
        vLayout.ForceExpandHeight.Value = false;

        InspectorUI.FixedRow(page, "Header", 42f, out var headerUi, Slot);
        headerUi.PushStyle();
        headerUi.FlexibleWidth(1f);
        _selectedTitle.Target = headerUi.Text("", InspectorUI.FontSize + 3f, InspectorUI.TextColor);
        InspectorUI.FillParent(_selectedTitle.Target.RectTransform!);
        _selectedTitle.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _selectedTitle.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        // Slot names are user content and may carry inline style tags; render them styled.
        _selectedTitle.Target.RichText.Value = true;
        headerUi.PopStyle();
        // Compact toolbar: short text labels on tight fixed widths (no icon art exists yet).
        InspectorUI.RelayButton(headerUi, this, "addchild", "+ Child", 78f);
        InspectorUI.RelayButton(headerUi, this, "insertparent", "+ Parent", 84f);
        InspectorUI.RelayButton(headerUi, this, "duplicate", "Dup", 50f);
        // Gizmo T/R/S mode switching lives on the radial CONTEXT MENU (GizmoModeMenuSource on the dev
        // tool) - spatial tool, spatial control. No header buttons for it here.
        headerUi.PushStyle();
        headerUi.TextColor(InspectorUI.DangerColor);
        InspectorUI.RelayButton(headerUi, this, "destroyassets", "Destroy Keep Assets", 150f);
        InspectorUI.RelayButton(headerUi, this, "destroy", "Destroy", 74f);
        headerUi.PopStyle();

        var bodyRow = page.AddSlot("Body");
        bodyRow.AttachComponent<RectTransform>();
        var bodyLE = bodyRow.AttachComponent<LayoutElement>();
        bodyLE.FlexibleHeight.Value = 1f;
        bodyLE.MinHeight.Value = 400f;
        var bodyLayout = bodyRow.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = 8f;
        bodyLayout.ForceExpandHeight.Value = true;
        bodyLayout.ForceExpandWidth.Value = false;

        var leftCol = bodyRow.AddSlot("Hierarchy");
        leftCol.AttachComponent<RectTransform>();
        var leftLE = leftCol.AttachComponent<LayoutElement>();
        leftLE.MinWidth.Value = 300f;
        leftLE.PreferredWidth.Value = 340f;
        leftLE.FlexibleWidth.Value = 0f;
        var leftImage = leftCol.AttachComponent<Image>();
        leftImage.Tint.Value = InspectorUI.PaneColor;
        var leftLayout = leftCol.AttachComponent<VerticalLayout>();
        leftLayout.Spacing.Value = 2f;
        leftLayout.PaddingLeft.Value = 4f;
        leftLayout.PaddingRight.Value = 4f;
        leftLayout.PaddingTop.Value = 4f;
        leftLayout.PaddingBottom.Value = 4f;
        leftLayout.ForceExpandWidth.Value = true;
        leftLayout.ForceExpandHeight.Value = false;

        InspectorUI.FixedRow(leftCol, "RootRow", 34f, out var rootUi, Slot);
        rootUi.PushStyle();
        rootUi.FlexibleWidth(1f);
        _rootTitle.Target = rootUi.Text("Root:", InspectorUI.FontSize + 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(_rootTitle.Target.RectTransform!);
        _rootTitle.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _rootTitle.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _rootTitle.Target.RichText.Value = true;
        rootUi.PopStyle();
        InspectorUI.RelayButton(rootUi, this, "rootup", "Up", 60f);

        _hierarchyContent.Target = BuildScrollList(leftCol, "Tree", TreeSpacing);

        var rightCol = bodyRow.AddSlot("Detail");
        rightCol.AttachComponent<RectTransform>();
        var rightLE = rightCol.AttachComponent<LayoutElement>();
        rightLE.FlexibleWidth.Value = 1f;
        rightLE.MinWidth.Value = 500f;
        var rightImage = rightCol.AttachComponent<Image>();
        rightImage.Tint.Value = InspectorUI.PaneColor;
        var rightLayout = rightCol.AttachComponent<VerticalLayout>();
        rightLayout.Spacing.Value = 3f;
        rightLayout.PaddingLeft.Value = 4f;
        rightLayout.PaddingRight.Value = 4f;
        rightLayout.PaddingTop.Value = 4f;
        rightLayout.PaddingBottom.Value = 4f;
        rightLayout.ForceExpandWidth.Value = true;
        rightLayout.ForceExpandHeight.Value = false;

        _componentsContent.Target = BuildScrollList(rightCol, "Sections", 3f);

        InspectorUI.FixedRow(page, "Footer", 42f, out var footerUi, Slot);
        footerUi.PushStyle();
        footerUi.FlexibleWidth(1f);
        InspectorUI.RelayButton(footerUi, this, "attach", "Attach Component...", 0f);
        footerUi.PopStyle();
    }

    private Slot BuildScrollList(Slot column, string name, float spacing)
    {
        var host = column.AddSlot(name);
        host.AttachComponent<RectTransform>();
        var hostLE = host.AttachComponent<LayoutElement>();
        hostLE.FlexibleHeight.Value = 1f;
        hostLE.MinHeight.Value = 200f;

        var scrollUi = new UIBuilder(host);
        InspectorUI.ApplyTheme(scrollUi, Slot);
        var scroll = scrollUi.ScrollRect(out var content, null, InspectorUI.PaneColor);
        InspectorUI.FillParent(scroll.Slot.GetComponent<RectTransform>()!);

        var contentLayout = content.Slot.AttachComponent<VerticalLayout>();
        contentLayout.Spacing.Value = spacing;
        contentLayout.ForceExpandWidth.Value = true;
        contentLayout.ForceExpandHeight.Value = false;
        return content.Slot;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;

        bool selectionChanged = Selected.GetWasChangedAndClear();
        if (selectionChanged)
        {
            _componentsDirty = true;
            UpdateSelectionGizmo();
            UpdateComponentSubscription();
        }
        if (Root.GetWasChangedAndClear())
            _hierarchyDirty = true;

        // A DIFFERENT tree (root change, "Up") is the only full rebuild left. Everything else below
        // reconciles the rows that actually moved.
        if (_hierarchyDirty)
        {
            _hierarchyDirty = false;
            RebuildHierarchy();
        }
        // A selection made outside the tree (a reference card, a gizmo, another tool) can point at a
        // slot whose row isn't built. Expanding its ancestor chain builds exactly those rows.
        if (selectionChanged)
            EnsureSelectionVisible();
        if (_expansionDirty)
        {
            _expansionDirty = false;
            SyncExpansion();
        }
        DrainChildSync();
        if (selectionChanged)
            UpdateSelectionHighlight();       // the tree just retints two rows, no rebuild
        if (_traceDirty)
        {
            _traceDirty = false;
            TraceHierarchyShape(Root.Target, 0);
        }
        if (_componentsDirty)
        {
            _componentsDirty = false;
            RebuildComponents();
        }
    }

    // Full rebuild: only for a tree rooted somewhere else, where nothing on screen can be reused.
    private void RebuildHierarchy()
    {
        var container = _hierarchyContent.Target;
        var root = Root.Target;
        if (container == null || container.IsDestroyed)
            return;

        var rootTitle = _rootTitle.Target;
        if (rootTitle != null && !rootTitle.IsDestroyed)
            rootTitle.Content.Value = $"Root: {root?.SlotName.Value ?? "<none>"}";

        // Rows left over from the previous pass would stack on top of the new ones; counted before the
        // clear so the trace below can tell "clear failed" apart from "the tree really has that shape".
        int staleRows = container.ChildCount + container.LocalChildCount;
        ClearRows();
        container.DestroyChildren();
        _highlightedId = Selected.Target?.ReferenceID.RawValue ?? 0;
        ClearRootNameSubscription();
        if (root == null || root.IsDestroyed)
            return;

        _rootNameField = root.SlotName;
        _rootNameHandler = value =>
        {
            var title = _rootTitle.Target;
            if (title != null && !title.IsDestroyed)
                title.Content.Value = $"Root: {value}";
        };
        _rootNameField.OnChanged += _rootNameHandler;

        BuildRow(container, root, 0, null, AncestorsPersistent(root), -1);
        InvalidateContentHeight();
        TraceHierarchyShape(root, staleRows);
    }

    // The tree is drawn from ONE slot, so a doubled row can only come from four places: the displayed
    // root has a parent that also renders, it appears twice in a parent's child list, it is its own
    // child, or the previous pass's rows survived the clear. This names which one, once per CHANGE of
    // shape (not per frame), so a bad tree identifies itself in the log instead of needing a repro.
    // Cheap: it runs once per reconcile pass, which is once per structural edit, and a repeat of the
    // same shape doesn't log at all. -xlinka
    private string _lastHierarchyTrace = "";

    private void TraceHierarchyShape(Slot? root, int staleRows)
    {
        if (root == null || root.IsDestroyed)
            return;
        var sb = new System.Text.StringBuilder();
        sb.Append("SceneInspector.Hierarchy: root='").Append(root.SlotName.Value)
          .Append("' id=").Append(root.ReferenceID)
          .Append(" local=").Append(root.IsLocalElement)
          .Append(" isWorldRoot=").Append(root.IsRootSlot)
          // Row count now comes from the row map, which is the tree: the content slot holds ONE
          // top-level node and the rest hang off it.
          .Append(" rows=").Append(_rows.Count)
          .Append(" staleBeforeClear=").Append(staleRows);

        sb.Append(" parents=");
        if (root.Parent == null)
        {
            sb.Append("<none>");
        }
        else
        {
            // Each step also says WHICH list the step below is filed in - a slot that is in neither
            // (MISSING) or in both is exactly the corruption a doubled row would need.
            int guard = 0;
            var child = root;
            for (var parent = root.Parent; parent != null && guard++ < 16; parent = parent.Parent)
            {
                sb.Append('\'').Append(parent.SlotName.Value).Append("'#").Append(parent.ReferenceID)
                  .Append(Contains(parent.Children, child) ? "[children]"
                        : Contains(parent.LocalChildren, child) ? "[localChildren]" : "[MISSING]")
                  .Append('/');
                child = parent;
            }
        }

        int duplicates = 0;
        bool selfChild = false;
        var children = root.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], root))
                selfChild = true;
            for (int j = i + 1; j < children.Count; j++)
            {
                if (ReferenceEquals(children[i], children[j]))
                    duplicates++;
            }
        }
        sb.Append(" dupChildren=").Append(duplicates).Append(" selfChild=").Append(selfChild);

        sb.Append(" children=[");
        for (int i = 0; i < children.Count && i < 24; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(children[i].SlotName.Value).Append('#').Append(children[i].ReferenceID);
        }
        if (children.Count > 24) sb.Append(", ...");
        sb.Append(']');

        var trace = sb.ToString();
        if (trace == _lastHierarchyTrace)
            return;
        _lastHierarchyTrace = trace;
        Lumora.Core.Logging.Logger.Log(trace);
    }

    private static bool Contains(System.Collections.Generic.IReadOnlyList<Slot> list, Slot slot)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], slot))
                return true;
        }
        return false;
    }

    private static bool AncestorsPersistent(Slot slot)
    {
        for (var parent = slot.Parent; parent != null; parent = parent.Parent)
        {
            if (!parent.Persistent.Value)
                return false;
        }
        return true;
    }

    private void ClearRootNameSubscription()
    {
        if (_rootNameField != null && _rootNameHandler != null)
            _rootNameField.OnChanged -= _rootNameHandler;
        _rootNameField = null;
        _rootNameHandler = null;
    }

    private void ClearTitleSubscription()
    {
        if (_titleNameField != null && _titleNameHandler != null)
            _titleNameField.OnChanged -= _titleNameHandler;
        _titleNameField = null;
        _titleNameHandler = null;
    }

    public override void OnDestroy()
    {
        if (_subscribedWorld != null)
        {
            _subscribedWorld.OnSlotAdded -= OnWorldSlotAdded;
            _subscribedWorld.OnSlotRemoved -= OnWorldSlotRemoved;
            _subscribedWorld = null;
        }
        ExpandedSlots.OnChanged -= OnExpandedSlotsChanged;
        ClearRows();
        ClearRootNameSubscription();
        ClearTitleSubscription();
        ClearSelectionGizmo();
        if (_componentWatchSlot != null)
        {
            _componentWatchSlot.OnComponentAdded -= OnSelectedComponentsChanged;
            _componentWatchSlot.OnComponentRemoved -= OnSelectedComponentsChanged;
            _componentWatchSlot = null;
        }
        base.OnDestroy();
    }

    // Selecting in the tree shows a transform gizmo on the slot in-world, so selection is spatial,
    // not just a highlighted row. Only a gizmo THIS panel spawned is destroyed on deselect - if the
    // user already had one on that slot (dev tool), selection borrows it and leaves it alone.
    private Slot? _gizmoTarget;
    private bool _gizmoOwned;

    private void UpdateSelectionGizmo()
    {
        var selected = Selected.Target;
        if (ReferenceEquals(selected, _gizmoTarget))
            return;

        ClearSelectionGizmo();

        if (selected == null || selected.IsDestroyed || selected.IsRootSlot)
            return;
        bool existed = Gizmos.GizmoHelper.HasGizmo(selected);
        var gizmo = Gizmos.GizmoHelper.SpawnGizmoFor(selected);
        if (gizmo == null)
            return;
        gizmo.LinkedInspector.Target = this;
        _gizmoTarget = selected;
        _gizmoOwned = !existed;
    }

    private void ClearSelectionGizmo()
    {
        if (_gizmoTarget != null && !_gizmoTarget.IsDestroyed && _gizmoOwned)
            Gizmos.GizmoHelper.DestroyGizmo(_gizmoTarget);
        _gizmoTarget = null;
        _gizmoOwned = false;
    }

    // Retint only the previously-highlighted and newly-selected rows. Selecting a slot must NOT
    // rebuild the tree.
    private void UpdateSelectionHighlight()
    {
        ulong newId = Selected.Target?.ReferenceID.RawValue ?? 0;
        if (newId == _highlightedId)
            return;
        if (_rows.TryGetValue(_highlightedId, out var previous))
        {
            if (previous.Background != null && !previous.Background.IsDestroyed)
                previous.Background.Tint.Value = color.Transparent;
            if (previous.Label != null && !previous.Label.IsDestroyed)
                previous.Label.Color.Value = previous.BaseColor;
        }
        if (_rows.TryGetValue(newId, out var current))
        {
            if (current.Background != null && !current.Background.IsDestroyed)
                current.Background.Tint.Value = InspectorUI.SelectionColor;
            if (current.Label != null && !current.Label.IsDestroyed)
                current.Label.Color.Value = InspectorUI.TextColor;
        }
        _highlightedId = newId;
    }

    // ---- incremental tree ------------------------------------------------------------------------

    private bool IsLiveRow(HierarchyRow row)
        => row.NodeSlot != null && !row.NodeSlot.IsDestroyed
           && _rows.TryGetValue(row.TargetId, out var mapped) && ReferenceEquals(mapped, row);

    private void QueueChildSync(ulong rowId)
    {
        if (rowId == 0 || !_rows.ContainsKey(rowId))
            return;
        if (_pendingChildSync.Add(rowId))
            MarkChangeDirty();
    }

    // One pass per OnChanges over the rows whose child set moved. Removals run across ALL of them
    // before any additions: a reparent is a remove on one container plus an add on another, and
    // pruning first is what guarantees a slot never owns two rows at once.
    private void DrainChildSync()
    {
        if (_pendingChildSync.Count == 0)
            return;

        _syncScratch.Clear();
        foreach (var id in _pendingChildSync)
        {
            if (_rows.TryGetValue(id, out var row))
                _syncScratch.Add(row);
        }
        _pendingChildSync.Clear();

        for (int i = 0; i < _syncScratch.Count; i++)
        {
            if (IsLiveRow(_syncScratch[i]))
                PruneRowChildren(_syncScratch[i]);
        }
        for (int i = 0; i < _syncScratch.Count; i++)
        {
            var row = _syncScratch[i];
            if (!IsLiveRow(row))
                continue;
            BuildRowChildren(row);
            UpdateExpanderGlyph(row);
        }
        _syncScratch.Clear();
        _traceDirty = true;
        InvalidateContentHeight();
    }

    // ExpandedSlots is the synced source of truth, so a toggle from any peer lands here: compare each
    // built row against the list and expand or collapse only the ones that disagree.
    private void SyncExpansion()
    {
        if (_rows.Count == 0)
            return;

        _syncScratch.Clear();
        foreach (var row in _rows.Values)
            _syncScratch.Add(row);
        for (int i = 0; i < _syncScratch.Count; i++)
        {
            var row = _syncScratch[i];
            if (!IsLiveRow(row))
                continue;
            bool wanted = row.Depth == 0 || IsExpanded(ExpandedSlots, row.TargetId);
            if (wanted == row.Expanded)
                continue;
            if (wanted)
                ExpandRow(row);
            else
                CollapseRow(row);
            _traceDirty = true;
        }
        _syncScratch.Clear();
        InvalidateContentHeight();
    }

    private void ExpandRow(HierarchyRow row)
    {
        if (row.Expanded)
            return;
        row.Expanded = true;
        PruneRowChildren(row);
        BuildRowChildren(row);
        UpdateExpanderGlyph(row);
    }

    private void CollapseRow(HierarchyRow row)
    {
        if (!row.Expanded)
            return;
        row.Expanded = false;
        DropChildContainer(row);
        UpdateExpanderGlyph(row);
    }

    // Collapse is ONE destroy: the child container takes the whole subtree's rows with it, and no row
    // outside this node is touched.
    private void DropChildContainer(HierarchyRow row)
    {
        for (int i = 0; i < row.Children.Count; i++)
            UnregisterSubtree(row.Children[i]);
        row.Children.Clear();
        var container = row.ChildContainer;
        row.ChildContainer = null;
        row.OverflowRow = null;
        row.OverflowCount = 0;
        if (container != null && !container.IsDestroyed)
            container.Destroy();
    }

    // Built when the row has children to show, destroyed the moment it doesn't. A node with only its
    // row in it adds no layout spacing of its own, which is what keeps the nested tree at the exact
    // row pitch the flat list had. -xlinka
    private static Slot CreateChildContainer(Slot node)
    {
        var container = node.AddSlot("Children");
        container.AttachComponent<RectTransform>();
        var layout = container.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = TreeSpacing;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        return container;
    }

    // Drop the rows whose slot is gone, went local, or moved to another parent. Runs before any
    // additions anywhere, so a reparented slot's old row is dead before its new one is built.
    private void PruneRowChildren(HierarchyRow row)
    {
        if (!row.Expanded || row.Target == null || row.Target.IsDestroyed)
            return;
        for (int i = row.Children.Count - 1; i >= 0; i--)
        {
            var child = row.Children[i];
            var target = child.Target;
            if (target != null && !target.IsDestroyed && !target.IsLocalElement
                && ReferenceEquals(target.Parent, row.Target))
                continue;
            row.Children.RemoveAt(i);
            DestroyRow(child);
        }
    }

    private void BuildRowChildren(HierarchyRow row)
    {
        var target = row.Target;
        if (!row.Expanded || target == null || target.IsDestroyed)
            return;
        if (!HasVisibleChildren(target))
        {
            // Expanded over nothing: hold no container at all, or the node layout's spacing opens a
            // gap under the row that the flat list never had.
            DropChildContainer(row);
            return;
        }
        if (row.ChildContainer == null || row.ChildContainer.IsDestroyed)
            row.ChildContainer = CreateChildContainer(row.NodeSlot);
        var container = row.ChildContainer;

        // Rows follow scene order: children sort by OrderOffset, ties keep their child-list order
        // (List.Sort is unstable, so the original index is the tiebreaker).
        var ordered = new List<(Slot Child, int Index)>();
        int childIndex = 0;
        foreach (var child in target.Children)
        {
            if (!child.IsLocalElement)
                ordered.Add((child, childIndex));
            childIndex++;
        }
        ordered.Sort(static (a, b) =>
        {
            int byOffset = a.Child.OrderOffset.Value.CompareTo(b.Child.OrderOffset.Value);
            return byOffset != 0 ? byOffset : a.Index.CompareTo(b.Index);
        });

        var existing = new Dictionary<ulong, HierarchyRow>(row.Children.Count);
        for (int i = 0; i < row.Children.Count; i++)
            existing[row.Children[i].TargetId] = row.Children[i];

        var placed = new List<HierarchyRow>(ordered.Count);
        int refused = 0;
        bool reordered = false;
        for (int i = 0; i < ordered.Count; i++)
        {
            ulong childId = ordered[i].Child.ReferenceID.RawValue;
            if (existing.TryGetValue(childId, out var childRow))
            {
                // An untouched child keeps its row, its whole subtree and its baked chunk; only its
                // position in the container's child list can change.
                if (childRow.NodeSlot.SiblingIndex != placed.Count)
                {
                    container.MoveChildToIndex(childRow.NodeSlot, placed.Count);
                    reordered = true;
                }
                placed.Add(childRow);
                continue;
            }
            // Budget gates NEW rows only. Rows already on screen stay and keep their place, so a full
            // tree doesn't shuffle when some other branch expands.
            if (_visibleRows >= RowBudget)
            {
                refused++;
                continue;
            }
            var built = BuildRow(container, ordered[i].Child, row.Depth + 1, row,
                row.EffectivePersistent, placed.Count);
            if (built != null)
                placed.Add(built);
        }

        // Anything left over is a row this container no longer owns; drop it rather than strand a
        // slot with two rows or a node slot with no row behind it.
        for (int i = 0; i < placed.Count; i++)
            existing.Remove(placed[i].TargetId);
        foreach (var orphan in existing.Values)
            DestroyRow(orphan);

        row.Children.Clear();
        row.Children.AddRange(placed);

        // Moving slots inside a child list is invisible to the layout until something says the
        // structure changed; adding or destroying a row signals itself through its RectTransform.
        if (reordered)
            container.GetComponent<RectTransform>()?.NotifyComponentsChanged();

        UpdateOverflowRow(row, refused);
    }

    // The marker the expansion budget leaves behind, so a subtree that didn't fully draw says so
    // where it was cut instead of going quietly short.
    private void UpdateOverflowRow(HierarchyRow row, int hidden)
    {
        var container = row.ChildContainer;
        if (row.OverflowCount == hidden)
        {
            if (hidden > 0 && row.OverflowRow != null && !row.OverflowRow.IsDestroyed
                && container != null && !container.IsDestroyed)
                container.MoveChildToIndex(row.OverflowRow, row.Children.Count);
            return;
        }
        if (row.OverflowRow != null && !row.OverflowRow.IsDestroyed)
            row.OverflowRow.Destroy();
        row.OverflowRow = null;
        row.OverflowCount = hidden;
        if (hidden <= 0 || container == null || container.IsDestroyed)
            return;

        var marker = InspectorUI.FixedRow(container, "More", RowHeight, out var markerUi, Slot);
        marker.GetComponent<HorizontalLayout>()!.PaddingLeft.Value = 4f + (row.Depth + 1) * IndentPerDepth;
        markerUi.PushStyle();
        markerUi.FlexibleWidth(1f);
        var text = markerUi.Text($"+{hidden} more", InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        markerUi.PopStyle();
        row.OverflowRow = marker;
    }

    private HierarchyRow? BuildRow(Slot container, Slot target, int depth, HierarchyRow? parent,
        bool ancestorsPersistent, int insertIndex)
    {
        if (target == null || target.IsDestroyed)
            return null;

        // Node wraps the row and, while expanded, the container its children live in. That nesting is
        // what makes collapse one destroy and leaves every sibling's row slot - and its baked chunk -
        // untouched; the flat look survives because the indent is paid as row padding per depth.
        var node = container.AddSlot("Node");
        node.AttachComponent<RectTransform>();
        var nodeLayout = node.AttachComponent<VerticalLayout>();
        nodeLayout.Spacing.Value = TreeSpacing;
        nodeLayout.ForceExpandWidth.Value = true;
        nodeLayout.ForceExpandHeight.Value = false;
        if (insertIndex >= 0)
            container.MoveChildToIndex(node, insertIndex);

        var row = new HierarchyRow
        {
            Target = target,
            TargetId = target.ReferenceID.RawValue,
            Depth = depth,
            Parent = parent,
            NodeSlot = node,
        };

        // A slot owns exactly one row. An entry already here means an add beat the old container's
        // prune; drop the old row rather than leave a duplicate in the tree.
        if (_rows.TryGetValue(row.TargetId, out var stale))
        {
            stale.Parent?.Children.Remove(stale);
            DestroyRow(stale);
        }
        _rows[row.TargetId] = row;
        _visibleRows++;

        BuildRowContent(row, ancestorsPersistent);

        if (depth == 0 || IsExpanded(ExpandedSlots, row.TargetId))
            ExpandRow(row);
        else
            UpdateExpanderGlyph(row);
        return row;
    }

    private void BuildRowContent(HierarchyRow row, bool ancestorsPersistent)
    {
        var target = row.Target;
        var rowSlot = InspectorUI.FixedRow(row.NodeSlot, "Row", RowHeight, out var ui, Slot);
        row.RowSlot = rowSlot;
        var rowBackground = rowSlot.AttachComponent<Image>();
        rowBackground.Tint.Value = color.Transparent;
        row.Background = rowBackground;
        rowSlot.GetComponent<HorizontalLayout>()!.PaddingLeft.Value = 4f + row.Depth * IndentPerDepth;

        // Gripping a tree row pulls a card for the slot.
        var rowSource = rowSlot.AttachComponent<ReferenceProxySource>();
        rowSource.Target.Target = target;

        // Releasing a held slot card over the row reparents that slot under this one (undoable).
        var reparent = rowSlot.AttachComponent<SlotReparentReceiver>();
        reparent.NewParent.Target = target;

        // Flat text rows: transparent expander/name buttons, the selection backing is the only fill.
        ui.PushStyle();
        ui.BackgroundColor(color.Transparent);

        // The expander cell is ALWAYS a button and goes blank when the slot has no children, so a
        // slot gaining or losing its first child only retexts one glyph. Swapping the cell for a
        // spacer would relayout the row and re-mesh its chunk on every add. -xlinka
        ui.PushStyle();
        ui.TextColor(InspectorUI.MutedColor);
        var expander = InspectorUI.RelayButton(ui, this, $"expand:{row.TargetId}", "", 28f);
        row.ExpanderLabel = expander.Slot.GetComponentInChildren<Text>();
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var nameButton = InspectorUI.RelayButton(ui, this, $"select:{row.TargetId}",
            target.SlotName.Value ?? "Slot", 0f);
        var nameText = nameButton.Slot.GetComponentInChildren<Text>();
        if (nameText != null)
        {
            nameText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
            // User content: slot names render their inline style tags.
            nameText.RichText.Value = true;
        }
        row.Label = nameText;
        ui.PopStyle();
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(28f);
        ui.PreferredWidth(28f);
        ui.FlexibleWidth(0f);
        var editorHost = ui.Next("Active");
        editorHost.AttachComponent<HorizontalLayout>();
        var editorUi = new UIBuilder(editorHost);
        InspectorUI.ApplyTheme(editorUi, Slot);
        editorHost.AttachComponent<BooleanMemberEditor>().Setup(target.ActiveSelf, "", editorUi);
        ui.PopStyle();

        ApplyRowTint(row, ancestorsPersistent);
        SubscribeRow(row);
    }

    // Persistence tints the label (bright orange = this slot opted out, muted orange = an ancestor
    // did), then effective inactivity fades whatever color that produced. Selection paints over both
    // and the cached base color restores them on deselect.
    private void ApplyRowTint(HierarchyRow row, bool ancestorsPersistent)
    {
        var target = row.Target;
        bool selfPersistent = target.Persistent.Value;
        row.EffectivePersistent = ancestorsPersistent && selfPersistent;
        color baseColor = row.EffectivePersistent
            ? InspectorUI.TextColor
            : selfPersistent ? InspectorUI.NonPersistentInheritedColor : InspectorUI.NonPersistentColor;
        if (!target.IsActive)
        {
            float fade = target.ActiveSelf.Value ? 0.45f : 0.30f;
            baseColor = new color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fade);
        }
        row.BaseColor = baseColor;

        bool selected = row.TargetId == (Selected.Target?.ReferenceID.RawValue ?? 0);
        if (row.Label != null && !row.Label.IsDestroyed)
            row.Label.Color.Value = selected ? InspectorUI.TextColor : baseColor;
        if (row.Background != null && !row.Background.IsDestroyed)
            row.Background.Tint.Value = selected ? InspectorUI.SelectionColor : color.Transparent;
    }

    private void RefreshRowTint(HierarchyRow row)
    {
        if (!IsLiveRow(row))
            return;
        ApplyRowTint(row, row.Parent?.EffectivePersistent ?? AncestorsPersistent(row.Target));
    }

    // Persistence is inherited, so a flip retints this row and every BUILT row under it. Rows under a
    // collapsed branch don't exist and pick the new tint up when they're built.
    private void RefreshRowTintTree(HierarchyRow row)
    {
        RefreshRowTint(row);
        for (int i = 0; i < row.Children.Count; i++)
            RefreshRowTintTree(row.Children[i]);
    }

    private static void UpdateExpanderGlyph(HierarchyRow row)
    {
        var label = row.ExpanderLabel;
        if (label == null || label.IsDestroyed || row.Target == null || row.Target.IsDestroyed)
            return;
        string glyph = HasVisibleChildren(row.Target) ? (row.Expanded ? "v" : ">") : "";
        if (label.Content.Value != glyph)
            label.Content.Value = glyph;
    }

    // Every row watches its OWN slot: child add/remove queues this row's container, an OrderOffset
    // change queues the PARENT's (a slot's offset decides where it sits among its siblings), and
    // name, active and persistent changes retext or retint in place. All of it dies with the row, so
    // a collapsed branch holds no subscriptions at all. -xlinka
    private void SubscribeRow(HierarchyRow row)
    {
        var target = row.Target;
        row.ChildAdded = (_, _) => QueueChildSync(row.TargetId);
        row.ChildRemoved = (_, _) => QueueChildSync(row.TargetId);
        row.OrderChanged = _ => QueueChildSync(row.Parent?.TargetId ?? 0);
        row.ActiveChanged = _ => RefreshRowTint(row);
        row.PersistentChanged = _ => RefreshRowTintTree(row);
        row.NameChanged = value =>
        {
            if (row.Label != null && !row.Label.IsDestroyed)
                row.Label.Content.Value = value ?? "Slot";
        };

        target.OnChildAdded += row.ChildAdded;
        target.OnChildRemoved += row.ChildRemoved;
        target.OrderOffsetChanged += row.OrderChanged;
        target.ActiveChanged += row.ActiveChanged;
        target.PersistentChanged += row.PersistentChanged;
        target.SlotName.OnChanged += row.NameChanged;
    }

    private static void UnsubscribeRow(HierarchyRow row)
    {
        var target = row.Target;
        if (target != null)
        {
            if (row.ChildAdded != null) target.OnChildAdded -= row.ChildAdded;
            if (row.ChildRemoved != null) target.OnChildRemoved -= row.ChildRemoved;
            if (row.OrderChanged != null) target.OrderOffsetChanged -= row.OrderChanged;
            if (row.ActiveChanged != null) target.ActiveChanged -= row.ActiveChanged;
            if (row.PersistentChanged != null) target.PersistentChanged -= row.PersistentChanged;
            if (row.NameChanged != null && target.SlotName != null)
                target.SlotName.OnChanged -= row.NameChanged;
        }
        row.ChildAdded = null;
        row.ChildRemoved = null;
        row.OrderChanged = null;
        row.ActiveChanged = null;
        row.PersistentChanged = null;
        row.NameChanged = null;
    }

    private void DestroyRow(HierarchyRow row)
    {
        var node = row.NodeSlot;
        UnregisterSubtree(row);
        if (node != null && !node.IsDestroyed)
            node.Destroy();
    }

    // Forget a row and everything under it: the slot destroy that follows takes the UI, this takes
    // the subscriptions and the budget back.
    private void UnregisterSubtree(HierarchyRow row)
    {
        for (int i = 0; i < row.Children.Count; i++)
            UnregisterSubtree(row.Children[i]);
        row.Children.Clear();
        UnsubscribeRow(row);
        if (_rows.TryGetValue(row.TargetId, out var mapped) && ReferenceEquals(mapped, row))
        {
            _rows.Remove(row.TargetId);
            _visibleRows--;
        }
        row.ChildContainer = null;
        row.OverflowRow = null;
        row.OverflowCount = 0;
    }

    private void ClearRows()
    {
        foreach (var row in _rows.Values)
        {
            UnsubscribeRow(row);
            row.Children.Clear();
        }
        _rows.Clear();
        _visibleRows = 0;
        _pendingChildSync.Clear();
        _syncScratch.Clear();
    }

    // A selection from outside the tree can sit under collapsed ancestors. Expand the chain between
    // the displayed root and the selection - those rows and nothing else.
    private void EnsureSelectionVisible()
    {
        var selected = Selected.Target;
        var root = Root.Target;
        if (selected == null || selected.IsDestroyed || root == null || root.IsDestroyed)
            return;
        if (_rows.ContainsKey(selected.ReferenceID.RawValue))
            return;

        bool underRoot = false;
        for (var s = selected; s != null; s = s.Parent)
        {
            if (ReferenceEquals(s, root))
            {
                underRoot = true;
                break;
            }
        }
        if (!underRoot)
            return;

        for (var s = selected.Parent; s != null; s = s.Parent)
        {
            ulong id = s.ReferenceID.RawValue;
            if (!IsExpanded(ExpandedSlots, id))
                ExpandedSlots.Add(id);
            if (ReferenceEquals(s, root))
                break;
        }
    }

    // The sizer latches on the row COUNT of its DIRECT children, and the tree hands it a single
    // top-level node - so a collapse (shorter, same count) would never re-pin. Poke it whenever the
    // shape changes. It re-measures on its next update and the ScrollRect holds its normalized
    // position, so the view doesn't jump. -xlinka
    private void InvalidateContentHeight()
    {
        var container = _hierarchyContent.Target;
        if (container == null || container.IsDestroyed)
            return;
        container.GetComponent<ScrollContentSizer>()?.Invalidate();
    }

    // Local-only children are render plumbing (canvas chunks, material caches); they never show.
    private static bool HasVisibleChildren(Slot slot)
    {
        foreach (var child in slot.Children)
        {
            if (!child.IsLocalElement)
                return true;
        }
        return false;
    }

    // Right pane: the slot's own member rows and action rows first, then one section per component.
    private void RebuildComponents()
    {
        var container = _componentsContent.Target;
        var selected = Selected.Target;
        if (container == null || container.IsDestroyed)
            return;

        var title = _selectedTitle.Target;
        if (title != null && !title.IsDestroyed)
            title.Content.Value = selected == null ? "Slot: <none>" : $"Slot: {selected.SlotName.Value}";

        ClearTitleSubscription();
        if (selected != null && !selected.IsDestroyed)
        {
            _titleNameField = selected.SlotName;
            _titleNameHandler = value =>
            {
                var titleText = _selectedTitle.Target;
                if (titleText != null && !titleText.IsDestroyed)
                    titleText.Content.Value = $"Slot: {value}";
            };
            _titleNameField.OnChanged += _titleNameHandler;
        }

        container.DestroyChildren();
        if (selected == null || selected.IsDestroyed)
            return;

        BuildSlotSection(container, selected);

        foreach (var component in selected.GetAllComponents())
        {
            if (component == null || component.IsDestroyed)
                continue;
            // Class-level opt-out: pure render plumbing renders no section at all.
            if (component.GetType().GetCustomAttribute<HideInInspectorAttribute>() != null)
                continue;
            BuildComponentSection(container, component);
        }
    }

    // Editable rows for the slot itself (Name, Parent, Tag, Active, Persistent, Position, Rotation,
    // Scale, OrderOffset), then the axis legend and the transform/parenting actions.
    private void BuildSlotSection(Slot container, Slot selected)
    {
        WorkerInspectorBuilder.BuildMemberRows(selected, container, Slot);

        // Column legend for the three-wide vector editors above: X red, Y green, Z blue. The spacer
        // matches the fixed chip + label width of member rows so the letters sit over the columns.
        InspectorUI.FixedRow(container, "AxisLegend", 24f, out var legendUi, Slot);
        legendUi.PushStyle();
        legendUi.MinWidth(210f);
        legendUi.PreferredWidth(210f);
        legendUi.FlexibleWidth(0f);
        legendUi.Empty("Spacer");
        legendUi.PopStyle();
        legendUi.PushStyle();
        legendUi.FlexibleWidth(1f);
        var axes = legendUi.Next("Axes");
        var axesLayout = axes.AttachComponent<HorizontalLayout>();
        axesLayout.Spacing.Value = 4f;
        axesLayout.ForceExpandHeight.Value = true;
        legendUi.NestInto(axes);
        AddAxisLabel(legendUi, "X", InspectorUI.AxisXColor);
        AddAxisLabel(legendUi, "Y", InspectorUI.AxisYColor);
        AddAxisLabel(legendUi, "Z", InspectorUI.AxisZColor);
        legendUi.NestOut();
        legendUi.PopStyle();

        InspectorUI.FixedRow(container, "ResetRow", 34f, out var resetUi, Slot);
        AddRowLabel(resetUi, "Reset:");
        InspectorUI.RelayButton(resetUi, this, "resetpos", "Position", 0f);
        InspectorUI.RelayButton(resetUi, this, "resetrot", "Rotation", 0f);
        InspectorUI.RelayButton(resetUi, this, "resetscale", "Scale", 0f);

        InspectorUI.FixedRow(container, "PivotRow", 34f, out var pivotUi, Slot);
        pivotUi.PushStyle();
        pivotUi.FlexibleWidth(1f);
        InspectorUI.RelayButton(pivotUi, this, "pivot", "Create Pivot At Center", 0f);
        pivotUi.PopStyle();

        InspectorUI.FixedRow(container, "FocusRow", 34f, out var focusUi, Slot);
        InspectorUI.RelayButton(focusUi, this, "jumpto", "Jump To", 0f);
        InspectorUI.RelayButton(focusUi, this, "bringto", "Bring To", 0f);

        InspectorUI.FixedRow(container, "ParentRow", 34f, out var parentUi, Slot);
        AddRowLabel(parentUi, "Parent under:");
        InspectorUI.RelayButton(parentUi, this, "parentuser", "Local User Space", 0f);
        InspectorUI.RelayButton(parentUi, this, "parentroot", "World Root", 0f);
    }

    private static void AddAxisLabel(UIBuilder ui, string label, color tint)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var text = ui.Text(label, InspectorUI.FontSize, tint);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    private static void AddRowLabel(UIBuilder ui, string label)
    {
        ui.PushStyle();
        ui.MinWidth(130f);
        ui.PreferredWidth(130f);
        ui.FlexibleWidth(0f);
        var text = ui.Text(label, InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    // Component section: header row (expander, type name, Enabled toggle, duplicate, destroy),
    // then the member rows when expanded.
    private void BuildComponentSection(Slot container, Component component)
    {
        ulong componentId = component.ReferenceID.RawValue;
        bool expanded = IsExpanded(ExpandedComponents, componentId);

        var header = InspectorUI.FixedRow(container, component.GetType().Name, 34f, out var ui, Slot);
        var headerImage = header.AttachComponent<Image>();
        headerImage.Tint.Value = InspectorUI.HeaderColor;

        // Gripping the header pulls a card for the component.
        var headerSource = header.AttachComponent<ReferenceProxySource>();
        headerSource.Target.Target = component;

        ui.PushStyle();
        ui.BackgroundColor(color.Transparent);
        ui.TextColor(InspectorUI.MutedColor);
        InspectorUI.RelayButton(ui, this, $"comp:{componentId}", expanded ? "v" : ">", 28f);
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var nameText = ui.Text(SyncMemberEditorBuilder.NiceTypeName(component.GetType()),
            InspectorUI.FontSize + 1f, InspectorUI.TextColor);
        InspectorUI.FillParent(nameText.RectTransform!);
        nameText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        nameText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        if (component.TryGetField("Enabled") is IField enabledField)
        {
            ui.PushStyle();
            ui.MinWidth(28f);
            ui.PreferredWidth(28f);
            ui.FlexibleWidth(0f);
            var editorHost = ui.Next("Enabled");
            editorHost.AttachComponent<HorizontalLayout>();
            var editorUi = new UIBuilder(editorHost);
            InspectorUI.ApplyTheme(editorUi, Slot);
            editorHost.AttachComponent<BooleanMemberEditor>().Setup(enabledField, "", editorUi);
            ui.PopStyle();
        }

        // Gizmo toggle, only for components something is actually registered to draw. Tinted while the
        // gizmo is up, so the header says whether pressing it puts one up or takes one down - a button
        // that looks identical in both states is the fastest way to end up with fifteen wireframes on
        // one object. -xlinka
        if (Gizmos.GizmoHelper.CanGizmo(component))
        {
            bool gizmoUp = Gizmos.GizmoHelper.HasComponentGizmo(component);
            ui.PushStyle();
            ui.TextColor(gizmoUp ? InspectorUI.CyanColor : InspectorUI.MutedColor);
            InspectorUI.RelayButton(ui, this, $"gizmo:{componentId}", "Giz", 42f);
            ui.PopStyle();
        }

        InspectorUI.RelayButton(ui, this, $"dupcomp:{componentId}", "Dup", 52f);

        ui.PushStyle();
        ui.TextColor(InspectorUI.DangerColor);
        InspectorUI.RelayButton(ui, this, $"remove:{componentId}", "X", 30f);
        ui.PopStyle();

        if (!expanded)
            return;

        if (component is ICustomInspector custom)
        {
            var customHost = container.AddSlot("Custom");
            customHost.AttachComponent<RectTransform>();
            var hostLE = customHost.AttachComponent<LayoutElement>();
            hostLE.MinHeight.Value = 60f;
            var customUi = new UIBuilder(customHost);
            InspectorUI.ApplyTheme(customUi, Slot);
            try { custom.BuildInspectorUI(customUi); }
            catch { /* logged by the component's own guards; leave the host empty */ }
        }
        else
        {
            WorkerInspectorBuilder.BuildMemberRows(component, container, Slot);
        }

        // Parameterless action methods reflect into clickable rows below the members.
        WorkerInspectorBuilder.BuildMethodRows(component, container, Slot);

        // Optional appended body (mesh statistics and the like), AFTER the reflected rows.
        if (component is ICustomInspectorUI extra)
        {
            var bodyHost = container.AddSlot("CustomBody");
            bodyHost.AttachComponent<RectTransform>();
            var bodyLayout = bodyHost.AttachComponent<VerticalLayout>();
            bodyLayout.Spacing.Value = 2f;
            bodyLayout.ForceExpandWidth.Value = true;
            bodyLayout.ForceExpandHeight.Value = false;
            var bodyUi = new UIBuilder(bodyHost);
            InspectorUI.ApplyTheme(bodyUi, Slot);
            try { extra.BuildInspectorBody(bodyUi); }
            catch { /* a broken body must not take the section down; leave the host empty */ }
        }
    }

    private static bool IsExpanded(SyncFieldList<ulong> list, ulong id)
    {
        foreach (var value in list)
        {
            if (value == id)
                return true;
        }
        return false;
    }

    private static void ToggleExpanded(SyncFieldList<ulong> list, ulong id)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == id)
            {
                list.RemoveAt(i);
                return;
            }
        }
        list.Add(id);
    }

    // destructive actions detour through a confirmation on the pressing user's radial menu;
    // everything else falls straight through to the plain handler
    public void HandleInspectorAction(string argument, UIInteractionContext context)
    {
        if (TryConfirmDestructiveAction(argument, context))
            return;
        HandleInspectorAction(argument);
    }

    private static readonly float[] DestroyConfirmFill = { 0.48f, 0.15f, 0.18f, 0.92f };

    private bool TryConfirmDestructiveAction(string argument, UIInteractionContext context)
    {
        string? title = null;
        if (argument == "destroy")
            title = $"Destroy {Selected.Target?.SlotName.Value ?? "slot"}?";
        else if (argument == "destroyassets")
            title = $"Destroy {Selected.Target?.SlotName.Value ?? "slot"}, keep assets?";
        else if (argument.StartsWith("remove:", StringComparison.Ordinal) && ulong.TryParse(argument[7..], out ulong id))
        {
            var component = World?.ReferenceController?.GetObjectOrNull(new RefID(id)) as Component;
            title = $"Destroy {component?.GetType().Name ?? "component"}?";
        }
        if (title == null)
            return false;

        var menu = context.Actor?.Root?.Slot?.GetComponentInChildren<UI.ContextMenuSystem>();
        if (menu == null || menu.IsOpen.Value)
            return false; // no menu to ask with - act immediately so the button never goes dead

        var ctx = MemberActionsRelay.BuildMenuContext(this, context.Actor);
        string confirmed = argument;
        menu.OpenConfirm(title, "Destroy", DestroyConfirmFill,
            () => { if (!IsDestroyed) HandleInspectorAction(confirmed); }, ctx);
        return true;
    }

    public void HandleInspectorAction(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return;

        if (argument.StartsWith("expand:", StringComparison.Ordinal) && ulong.TryParse(argument[7..], out ulong slotId))
        {
            // The expander cell exists on every row (blank when childless) so gaining a first child
            // only retexts a glyph - so refuse the toggle rather than park a dead id in the list.
            if (World?.ReferenceController?.GetObjectOrNull(new RefID(slotId)) is Slot expandTarget
                && !HasVisibleChildren(expandTarget))
                return;
            // The list's own change event drives the reconcile, which expands or collapses THAT row.
            ToggleExpanded(ExpandedSlots, slotId);
            RunApplyChangesSafe();
            return;
        }
        if (argument.StartsWith("select:", StringComparison.Ordinal) && ulong.TryParse(argument[7..], out ulong selId))
        {
            // Just set the synced field. OnChanges catches the change and retints two rows +
            // rebuilds the detail pane - NO hierarchy rebuild.
            if (World?.ReferenceController?.GetObjectOrNull(new RefID(selId)) is Slot slot)
                Selected.Target = slot;
            RunApplyChangesSafe();
            return;
        }
        if (argument.StartsWith("comp:", StringComparison.Ordinal) && ulong.TryParse(argument[5..], out ulong compId))
        {
            ToggleExpanded(ExpandedComponents, compId);
            _componentsDirty = true;
            RunApplyChangesSafe();
            return;
        }
        if (argument.StartsWith("remove:", StringComparison.Ordinal) && ulong.TryParse(argument[7..], out ulong remId))
        {
            // Serialize-then-destroy so the X is undoable (the batch re-attaches + loads the state back).
            if (World?.ReferenceController?.GetObjectOrNull(new RefID(remId)) is Component component)
                ComponentUndo.RecordDestroy(this, component);
            _componentsDirty = true;
            RunApplyChangesSafe();
            return;
        }
        if (argument.StartsWith("gizmo:", StringComparison.Ordinal) && ulong.TryParse(argument[6..], out ulong gizId))
        {
            // Explicit only: selecting a slot never spawns these, so the header button is the one way
            // in and the one way back out.
            if (World?.ReferenceController?.GetObjectOrNull(new RefID(gizId)) is Component gizmoTarget)
                Gizmos.GizmoHelper.ToggleComponentGizmo(gizmoTarget);
            // Rebuild the sections so the button re-tints to whatever the toggle just left behind.
            _componentsDirty = true;
            RunApplyChangesSafe();
            return;
        }
        if (argument.StartsWith("dupcomp:", StringComparison.Ordinal) && ulong.TryParse(argument[8..], out ulong dupId))
        {
            if (World?.ReferenceController?.GetObjectOrNull(new RefID(dupId)) is Component source
                && source.Slot != null && !source.Slot.IsDestroyed)
            {
                var clone = source.Slot.AttachComponent(source.GetType());
                clone.CopyValues(source);
                ComponentUndo.RecordAttach(this, clone);
            }
            _componentsDirty = true;
            RunApplyChangesSafe();
            return;
        }

        switch (argument)
        {
            case "rootup":
            {
                var root = Root.Target;
                if (root == null || root.IsRootSlot || root.Parent == null) return;
                Root.Target = root.Parent;
                break;
            }
            case "addchild":
            {
                var selected = Selected.Target;
                if (selected == null) return;
                var child = selected.AddSlot("Slot");
                RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { child }, "Add Child"));
                if (!IsExpanded(ExpandedSlots, selected.ReferenceID.RawValue))
                    ExpandedSlots.Add(selected.ReferenceID.RawValue);
                Selected.Target = child;
                break;
            }
            case "insertparent":
            {
                InsertParentAboveSelected();
                break;
            }
            case "pivot":
            {
                CreatePivotAboveSelected();
                break;
            }
            case "duplicate":
            {
                var selected = Selected.Target;
                if (selected == null || selected == Root.Target) return;
                var copy = selected.Duplicate();
                if (copy == null) return;
                RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { copy }, "Duplicate"));
                Selected.Target = copy;
                break;
            }
            case "destroy":
            {
                var selected = Selected.Target;
                if (selected == null || selected == Root.Target || selected.IsRootSlot) return;
                var parent = selected.Parent;
                // Parked in the graveyard instead of destroyed, so the step reverses cleanly.
                // Only park when a history exists to evict it later; otherwise destroy for real.
                var manager = FindUndoManager();
                var batch = manager != null ? SlotExistenceUndoBatch.Destroy(World, new[] { selected }) : null;
                if (batch != null)
                    manager!.Record(batch);
                else
                    selected.Destroy();
                Selected.Target = parent;
                break;
            }
            case "destroyassets":
            {
                var selected = Selected.Target;
                if (selected == null || selected == Root.Target || selected.IsRootSlot) return;
                var parent = selected.Parent;
                // The recording variant parks emptied slots and serializes stripped components so the
                // step reverses; it declines when there is nowhere to park, and then the plain strip
                // runs instead so the button is never dead.
                var manager = FindUndoManager();
                var batch = manager != null ? PreserveAssetsUndoBatch.Perform(selected) : null;
                if (batch != null)
                    manager!.Record(batch);
                else
                    selected.DestroyPreservingAssets();
                Selected.Target = parent;
                break;
            }
            case "resetpos":
            {
                // Editors watch the field and refresh in place; no rebuild needed.
                var selected = Selected.Target;
                if (selected == null || selected.IsRootSlot) return;
                var undo = SlotTransformUndoBatch.Begin(selected, "Reset Position");
                selected.LocalPosition.Value = float3.Zero;
                RecordUndo(undo?.Commit());
                return;
            }
            case "resetrot":
            {
                var selected = Selected.Target;
                if (selected == null || selected.IsRootSlot) return;
                var undo = SlotTransformUndoBatch.Begin(selected, "Reset Rotation");
                selected.LocalRotation.Value = floatQ.Identity;
                RecordUndo(undo?.Commit());
                return;
            }
            case "resetscale":
            {
                var selected = Selected.Target;
                if (selected == null || selected.IsRootSlot) return;
                var undo = SlotTransformUndoBatch.Begin(selected, "Reset Scale");
                selected.LocalScale.Value = float3.One;
                RecordUndo(undo?.Commit());
                return;
            }
            case "jumpto":
            {
                JumpToSelected();
                return;
            }
            case "bringto":
            {
                var selected = Selected.Target;
                var head = World?.LocalUser?.Root?.HeadSlot;
                if (selected == null || selected.IsRootSlot || head == null) return;
                var undo = SlotTransformUndoBatch.Begin(selected, "Bring To");
                var forward = head.GlobalRotation * float3.Backward; // view forward is -Z
                selected.GlobalPosition = head.GlobalPosition + forward * 0.6f;
                RecordUndo(undo?.Commit());
                return;
            }
            case "parentuser":
            {
                ReparentSelected(World?.LocalUser?.Root?.Slot, "Parent Under Local User Space");
                break;
            }
            case "parentroot":
            {
                ReparentSelected(World?.RootSlot, "Parent Under World Root");
                break;
            }
            case "attach":
            {
                var selected = Selected.Target;
                if (selected == null) return;
                var existing = _selector.Target;
                if (existing != null && !existing.IsDestroyed)
                    existing.TargetSlot.Target = selected;
                else
                    _selector.Target = ComponentSelectorPanel.Spawn(this, selected);
                return;
            }
        }
        // No hierarchy flag: every structural edit above (add child, duplicate, destroy, reparent,
        // insert parent, pivot) reaches the tree as a child add/remove on the affected row. A new
        // ROOT is the exception and rides Root's own changed flag.
        _componentsDirty = true;
        RunApplyChangesSafe();
    }

    // Keep the object where it stands in the world while changing who owns it.
    private void ReparentSelected(Slot? newParent, string description)
    {
        var selected = Selected.Target;
        if (selected == null || newParent == null || selected.IsRootSlot)
            return;
        if (newParent == selected || newParent.IsDescendantOf(selected))
            return;
        var undo = SlotTransformUndoBatch.Begin(selected, description);
        selected.SetParent(newParent, preserveGlobalTransform: true);
        RecordUndo(undo?.Commit());
    }

    // Wrap the selected slot in a fresh parent that sits at its exact pose; the slot's local
    // transform collapses to identity underneath it.
    private void InsertParentAboveSelected()
    {
        var target = Selected.Target;
        if (target == null || target.IsRootSlot || target.Parent == null)
            return;

        var wrapper = target.Parent.AddSlot(target.SlotName.Value + " - Parent");
        wrapper.LocalPosition.Value = target.LocalPosition.Value;
        wrapper.LocalRotation.Value = target.LocalRotation.Value;
        wrapper.LocalScale.Value = target.LocalScale.Value;

        var created = SlotExistenceUndoBatch.Created(World, new[] { wrapper }, "Insert Parent");
        var move = SlotTransformUndoBatch.Begin(target, "Insert Parent");
        target.SetParent(wrapper);
        target.LocalPosition.Value = float3.Zero;
        target.LocalRotation.Value = floatQ.Identity;
        target.LocalScale.Value = float3.One;
        RecordUndo(CompositeUndoBatch.Combine("Insert Parent", created, move?.Commit()));

        if (Root.Target == target)
            Root.Target = wrapper;
        Selected.Target = wrapper;
    }

    // New parent at the center of the subtree's visual bounds, keeping the object where it is,
    // so grab/scale/rotate operate around the middle instead of an arbitrary authored origin.
    private void CreatePivotAboveSelected()
    {
        var target = Selected.Target;
        if (target == null || target.IsRootSlot || target.Parent == null)
            return;
        if (!SlotBoundsHelper.TryComputeWorldBounds(target, out var bounds))
            return;
        var center = bounds.Center;
        if (float3.DistanceSquared(center, target.GlobalPosition) < 1e-10f)
            return;

        var wrapper = target.Parent.AddSlot(target.SlotName.Value + " - Pivot");
        wrapper.GlobalPosition = center;
        wrapper.GlobalRotation = target.GlobalRotation;

        var created = SlotExistenceUndoBatch.Created(World, new[] { wrapper }, "Create Pivot");
        var move = SlotTransformUndoBatch.Begin(target, "Create Pivot");
        target.SetParent(wrapper, preserveGlobalTransform: true);
        RecordUndo(CompositeUndoBatch.Combine("Create Pivot", created, move?.Commit()));

        if (Root.Target == target)
            Root.Target = wrapper;
        Selected.Target = wrapper;
    }

    // Teleport the local user to stand a step back from the slot, approaching from where they
    // already are. Goes through the character controller so physics state stays coherent.
    private void JumpToSelected()
    {
        var selected = Selected.Target;
        var userRoot = World?.LocalUser?.Root;
        if (selected == null || userRoot == null)
            return;
        var character = userRoot.GetRegisteredComponent<CharacterController>();
        if (character == null)
            return;

        var away = userRoot.Slot.GlobalPosition - selected.GlobalPosition;
        away.y = 0f;
        away = away.LengthSquared > 1e-6f ? away.Normalized : float3.Backward;
        character.Teleport(selected.GlobalPosition + away * 1.5f);
    }

    private UndoManager? FindUndoManager()
        => World?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();

    private void RecordUndo(IUndoBatch? batch)
    {
        if (batch != null)
            FindUndoManager()?.Record(batch);
    }

    // selector calls this after attaching so the component list refreshes
    public void MarkListsDirty()
    {
        // Attaching a component can't change the slot tree; any slots it spawns arrive as child
        // events on the row that owns them.
        _componentsDirty = true;
        RunApplyChangesSafe();
    }

    private void RunApplyChangesSafe()
    {
        // The rebuild itself runs in OnChanges on the authority; a synced member changed in every
        // action path above, which marks the component dirty on all peers including the authority.
        MarkChangeDirty();
    }
}
