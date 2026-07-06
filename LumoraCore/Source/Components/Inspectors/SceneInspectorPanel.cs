// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>Receives inspector row-button actions routed through InspectorButtonRelay.</summary>
public interface IInspectorActionHandler
{
    void HandleInspectorAction(string argument);
}

/// <summary>
/// The in-world scene inspector. Layout follows the proven dialog idiom exactly: rows are AddSlot +
/// RectTransform + LayoutElement(Min/Preferred) + HorizontalLayout, graphics attach to sized slots,
/// texts get fill anchors. Hierarchy and component lists are FLAT sequences of fixed-height rows
/// (expansion state is synced and toggles rebuild the list) - no nested variable-height sizing.
/// The authority builds the replicated UI; selection/expansion are synced so all peers agree.
/// </summary>
[ComponentCategory("Utility/Inspectors")]
public class SceneInspectorPanel : Component, IInspectorActionHandler
{
    public readonly SyncRef<Slot> Root;
    public readonly SyncRef<Slot> Selected;
    public readonly SyncFieldList<ulong> ExpandedSlots;
    public readonly SyncFieldList<ulong> ExpandedComponents;

    private readonly SyncRef<Slot> _hierarchyContent;
    private readonly SyncRef<Slot> _componentsContent;
    private readonly SyncRef<Text> _selectedTitle;
    private readonly SyncRef<ComponentSelectorPanel> _selector;

    private bool _hierarchyDirty = true;
    private bool _componentsDirty = true;

    // Selection highlight without rebuilding: cache each row's name text by slot id (refilled on
    // every hierarchy rebuild). Selecting then recolors exactly two labels - with per-row chunks that
    // re-meshes two rows, not the whole tree. This is what "touch one thing, update one thing" needs.
    private readonly System.Collections.Generic.Dictionary<ulong, Text> _rowNameTexts = new();
    private ulong _highlightedId;

    public SceneInspectorPanel()
    {
        Root = new SyncRef<Slot>(this);
        Selected = new SyncRef<Slot>(this);
        ExpandedSlots = new SyncFieldList<ulong>(this);
        ExpandedComponents = new SyncFieldList<ulong>(this);
        _hierarchyContent = new SyncRef<Slot>(this);
        _componentsContent = new SyncRef<Slot>(this);
        _selectedTitle = new SyncRef<Text>(this);
        _selector = new SyncRef<ComponentSelectorPanel>(this);
    }

    /// <summary>Spawn an inspector in front of the local user, rooted at the given slot.</summary>
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
            panelSlot.GlobalPosition = head.GlobalPosition + forward * 1.0f;
            float yaw = MathF.Atan2(forward.x, forward.z);
            panelSlot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, yaw);
        }
        panelSlot.LocalScale.Value = float3.One * 0.0005f;

        var panel = panelSlot.AttachComponent<SceneInspectorPanel>();
        panel.Root.Target = rootTarget;
        panel.Selected.Target = rootTarget;
        return panel;
    }

    public override void OnAttach()
    {
        base.OnAttach();

        var theme = Slot.GetOrAttachComponent<UITheme>();
        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Inspector";
        shell.Size.Value = new float2(1300f, 1500f);
        theme.ApplyTo(shell);

        shell.RebuildContent(BuildLayout);
        _hierarchyDirty = true;
        _componentsDirty = true;
    }

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        var theme = InspectorUI.ApplyTheme(ui, Slot);

        var vLayout = page.AttachComponent<VerticalLayout>();
        vLayout.Spacing.Value = 8f;
        vLayout.PaddingLeft.Value = 8f;
        vLayout.PaddingRight.Value = 8f;
        vLayout.PaddingTop.Value = 8f;
        vLayout.PaddingBottom.Value = 8f;
        vLayout.ForceExpandWidth.Value = true;
        vLayout.ForceExpandHeight.Value = false;

        // Header row: selected slot name + slot actions.
        var headerRow = InspectorUI.FixedRow(page, "Header", 42f, out var headerUi, Slot);
        headerUi.PushStyle();
        headerUi.FlexibleWidth(1f);
        _selectedTitle.Target = headerUi.Text("", InspectorUI.FontSize + 3f, InspectorUI.TextColor);
        InspectorUI.FillParent(_selectedTitle.Target.RectTransform!);
        _selectedTitle.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _selectedTitle.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        headerUi.PopStyle();
        InspectorUI.RelayButton(headerUi, this, "addchild", "+ Child", 110f);
        InspectorUI.RelayButton(headerUi, this, "duplicate", "Duplicate", 110f);
        headerUi.PushStyle();
        headerUi.TextColor(InspectorUI.DangerColor);
        InspectorUI.RelayButton(headerUi, this, "destroy", "Destroy", 100f);
        headerUi.PopStyle();

        // Body row: flexible height, two columns.
        var bodyRow = page.AddSlot("Body");
        bodyRow.AttachComponent<RectTransform>();
        var bodyLE = bodyRow.AttachComponent<LayoutElement>();
        bodyLE.FlexibleHeight.Value = 1f;
        bodyLE.MinHeight.Value = 400f;
        var bodyLayout = bodyRow.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = 8f;
        bodyLayout.ForceExpandHeight.Value = true;
        bodyLayout.ForceExpandWidth.Value = false;

        // Left column: hierarchy list.
        var leftCol = bodyRow.AddSlot("Hierarchy");
        leftCol.AttachComponent<RectTransform>();
        var leftLE = leftCol.AttachComponent<LayoutElement>();
        leftLE.MinWidth.Value = 380f;
        leftLE.PreferredWidth.Value = 460f;
        leftLE.FlexibleWidth.Value = 0f;
        var leftImage = leftCol.AttachComponent<Image>();
        leftImage.Tint.Value = new color(0.09f, 0.11f, 0.15f, 0.9f);
        var leftLayout = leftCol.AttachComponent<VerticalLayout>();
        leftLayout.Spacing.Value = 2f;
        leftLayout.PaddingLeft.Value = 4f;
        leftLayout.PaddingRight.Value = 4f;
        leftLayout.PaddingTop.Value = 4f;
        leftLayout.ForceExpandWidth.Value = true;
        leftLayout.ForceExpandHeight.Value = false;
        _hierarchyContent.Target = leftCol;

        // Right column: component list.
        var rightCol = bodyRow.AddSlot("Components");
        rightCol.AttachComponent<RectTransform>();
        var rightLE = rightCol.AttachComponent<LayoutElement>();
        rightLE.FlexibleWidth.Value = 1f;
        rightLE.MinWidth.Value = 500f;
        var rightImage = rightCol.AttachComponent<Image>();
        rightImage.Tint.Value = new color(0.10f, 0.12f, 0.17f, 0.9f);
        var rightLayout = rightCol.AttachComponent<VerticalLayout>();
        rightLayout.Spacing.Value = 3f;
        rightLayout.PaddingLeft.Value = 4f;
        rightLayout.PaddingRight.Value = 4f;
        rightLayout.PaddingTop.Value = 4f;
        rightLayout.ForceExpandWidth.Value = true;
        rightLayout.ForceExpandHeight.Value = false;
        _componentsContent.Target = rightCol;

        // Footer row: attach component.
        var footerRow = InspectorUI.FixedRow(page, "Footer", 42f, out var footerUi, Slot);
        footerUi.PushStyle();
        footerUi.FlexibleWidth(1f);
        InspectorUI.RelayButton(footerUi, this, "attach", "Attach Component...", 0f);
        footerUi.PopStyle();
        _ = headerRow;
        _ = footerRow;
        _ = theme;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;

        if (Selected.GetWasChangedAndClear())
        {
            _componentsDirty = true;      // the right pane shows the new slot's components
            UpdateSelectionHighlight();   // the tree just recolors two labels, no rebuild
        }
        if (Root.GetWasChangedAndClear())
            _hierarchyDirty = true;

        // Rebuild only the side that changed - a component expand must not re-tessellate the tree.
        if (_hierarchyDirty)
        {
            _hierarchyDirty = false;
            RebuildHierarchy();
        }
        if (_componentsDirty)
        {
            _componentsDirty = false;
            RebuildComponents();
        }
    }

    // FLAT hierarchy: visible slots depth-first, one fixed-height row each, indent by depth.
    private void RebuildHierarchy()
    {
        var container = _hierarchyContent.Target;
        var root = Root.Target;
        if (container == null || container.IsDestroyed || root == null || root.IsDestroyed)
            return;

        container.DestroyChildren();
        _rowNameTexts.Clear();
        _highlightedId = 0;
        int rowBudget = 40; // clip long trees rather than overflow the panel (scroll comes later)
        BuildHierarchyRows(container, root, 0, ref rowBudget);
        UpdateSelectionHighlight();
    }

    // Recolor only the previously-highlighted and newly-selected row labels. Selecting a slot must
    // NOT rebuild the tree - recolor the affected rows in place instead.
    private void UpdateSelectionHighlight()
    {
        ulong newId = Selected.Target?.ReferenceID.RawValue ?? 0;
        if (newId == _highlightedId)
            return;
        if (_rowNameTexts.TryGetValue(_highlightedId, out var previous) && previous != null && !previous.IsDestroyed)
            previous.Color.Value = InspectorUI.TextColor;
        if (_rowNameTexts.TryGetValue(newId, out var current) && current != null && !current.IsDestroyed)
            current.Color.Value = InspectorUI.AccentColor;
        _highlightedId = newId;
    }

    private void BuildHierarchyRows(Slot container, Slot slot, int depth, ref int budget)
    {
        if (budget-- <= 0 || slot == null || slot.IsDestroyed)
            return;

        ulong slotId = slot.ReferenceID.RawValue;
        bool expanded = IsExpanded(ExpandedSlots, slotId) || depth == 0;
        bool hasChildren = slot.ChildCount > 0;

        var row = InspectorUI.FixedRow(container, "Row", 30f, out var ui, Slot);
        var rowLayout = row.GetComponent<HorizontalLayout>()!;
        rowLayout.PaddingLeft.Value = 4f + depth * 18f;

        InspectorUI.RelayButton(ui, this, $"expand:{slotId}",
            !hasChildren ? "-" : expanded ? "v" : ">", 28f);

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var nameButton = InspectorUI.RelayButton(ui, this, $"select:{slotId}",
            slot.SlotName.Value ?? "Slot", 0f);
        var nameText = nameButton.Slot.GetComponentInChildren<Text>();
        if (nameText != null)
            _rowNameTexts[slotId] = nameText;
        ui.PopStyle();

        if (slot.TryGetField("ActiveSelf") is IField activeField)
        {
            ui.PushStyle();
            ui.MinWidth(28f);
            ui.PreferredWidth(28f);
            ui.FlexibleWidth(0f);
            var editorHost = ui.Next("Active");
            editorHost.AttachComponent<HorizontalLayout>();
            var editorUi = new UIBuilder(editorHost);
            InspectorUI.ApplyTheme(editorUi, Slot);
            editorHost.AttachComponent<BooleanMemberEditor>().Setup(activeField, "", editorUi);
            ui.PopStyle();
        }

        if (expanded)
        {
            foreach (var child in slot.Children)
                BuildHierarchyRows(container, child, depth + 1, ref budget);
        }
    }

    // FLAT component list: header row per component; expanded ones get their member rows below.
    private void RebuildComponents()
    {
        var container = _componentsContent.Target;
        var selected = Selected.Target;
        if (container == null || container.IsDestroyed)
            return;

        var title = _selectedTitle.Target;
        if (title != null && !title.IsDestroyed)
            title.Content.Value = selected?.SlotName.Value ?? "";

        container.DestroyChildren();
        if (selected == null || selected.IsDestroyed)
            return;

        foreach (var component in selected.GetAllComponents())
        {
            if (component == null || component.IsDestroyed)
                continue;

            bool expanded = IsExpanded(ExpandedComponents, component.ReferenceID.RawValue);

            var header = InspectorUI.FixedRow(container, component.GetType().Name, 34f, out var ui, Slot);
            var headerImage = header.AttachComponent<Image>();
            headerImage.Tint.Value = InspectorUI.HeaderColor;

            InspectorUI.RelayButton(ui, this, $"comp:{component.ReferenceID.RawValue}", expanded ? "v" : ">", 28f);

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

            ui.PushStyle();
            ui.TextColor(InspectorUI.DangerColor);
            InspectorUI.RelayButton(ui, this, $"remove:{component.ReferenceID.RawValue}", "X", 30f);
            ui.PopStyle();

            if (expanded)
            {
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
            }
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

    public void HandleInspectorAction(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return;

        if (argument.StartsWith("expand:", StringComparison.Ordinal) && ulong.TryParse(argument[7..], out ulong slotId))
        {
            ToggleExpanded(ExpandedSlots, slotId);
            _hierarchyDirty = true;
            RunApplyChangesSafe();
            return;
        }
        if (argument.StartsWith("select:", StringComparison.Ordinal) && ulong.TryParse(argument[7..], out ulong selId))
        {
            // Just set the synced field. OnChanges catches the change and recolors two labels +
            // rebuilds the component pane - NO hierarchy rebuild.
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
            if (World?.ReferenceController?.GetObjectOrNull(new RefID(remId)) is Component component)
                component.Destroy();
            _componentsDirty = true;
            RunApplyChangesSafe();
            return;
        }

        switch (argument)
        {
            case "addchild":
            {
                var selected = Selected.Target;
                if (selected == null) return;
                var child = selected.AddSlot("Slot");
                if (!IsExpanded(ExpandedSlots, selected.ReferenceID.RawValue))
                    ExpandedSlots.Add(selected.ReferenceID.RawValue);
                Selected.Target = child;
                break;
            }
            case "duplicate":
            {
                var selected = Selected.Target;
                if (selected == null || selected == Root.Target) return;
                var copy = selected.Duplicate();
                if (copy != null)
                    Selected.Target = copy;
                break;
            }
            case "destroy":
            {
                var selected = Selected.Target;
                if (selected == null || selected == Root.Target) return;
                var parent = selected.Parent;
                selected.Destroy();
                Selected.Target = parent;
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
        _hierarchyDirty = true;
        _componentsDirty = true;
        RunApplyChangesSafe();
    }

    /// <summary>Selector calls this after attaching so the component list refreshes.</summary>
    public void MarkListsDirty()
    {
        _hierarchyDirty = true;
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
