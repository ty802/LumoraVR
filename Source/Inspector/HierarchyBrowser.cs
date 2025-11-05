using System.Collections.Generic;
using Godot;
using Aquamarine.Source.Core;

namespace Aquamarine.Source.Inspector;

/// <summary>
/// Hierarchical browser for viewing and selecting Slots in a World.
/// </summary>
public partial class HierarchyBrowser : Control
{
    private Tree _tree;
    private World _currentWorld;
    private readonly Dictionary<Slot, TreeItem> _slotToTreeItem = new();
    private Slot _selectedSlot;

    [Signal]
    public delegate void SlotSelectedEventHandler(Slot slot);

    [Signal]
    public delegate void SlotReparentedEventHandler(Slot slot, Slot newParent);

    public override void _Ready()
    {
        SetupUI();
    }

    private void SetupUI()
    {
        // Create tree view
        _tree = new Tree();
        _tree.HideRoot = false;
        _tree.AllowRmbSelect = true;
        _tree.AllowReselect = true;
        _tree.CustomMinimumSize = new Vector2(250, 400);

        AddChild(_tree);

        // Connect signals
        _tree.ItemSelected += OnItemSelected;
        _tree.ItemCollapsed += OnItemCollapsed;
    }

    /// <summary>
    /// Set the World to display in the hierarchy.
    /// </summary>
    public void SetWorld(World world)
    {
        _currentWorld = world;
        RefreshHierarchy();

        // Subscribe to world events
        if (world != null)
        {
            world.OnSlotAdded += OnSlotAdded;
            world.OnSlotRemoved += OnSlotRemoved;
        }
    }

    /// <summary>
    /// Refresh the entire hierarchy tree.
    /// </summary>
    public void RefreshHierarchy()
    {
        _tree.Clear();
        _slotToTreeItem.Clear();

        if (_currentWorld?.RootSlot == null) return;

        var root = _tree.CreateItem();
        root.SetText(0, _currentWorld.WorldName.Value);

        BuildTree(_currentWorld.RootSlot, root);
    }

    /// <summary>
    /// Recursively build the tree structure.
    /// </summary>
    private void BuildTree(Slot slot, TreeItem parentItem)
    {
        var item = _tree.CreateItem(parentItem);
        item.SetText(0, GetSlotDisplayName(slot));
        item.SetMeta("slot", slot);

        // Store mapping
        _slotToTreeItem[slot] = item;

        // Set icon based on slot state
        UpdateSlotIcon(slot, item);

        // Recursively add children
        foreach (var child in slot.Children)
        {
            BuildTree(child, item);
        }
    }

    private string GetSlotDisplayName(Slot slot)
    {
        var name = slot.SlotName.Value;

        // Add component count indicator
        var componentCount = slot.Components.Count;
        if (componentCount > 0)
        {
            name += $" [{componentCount}]";
        }

        // Add tag if present
        if (!string.IsNullOrEmpty(slot.Tag.Value))
        {
            name += $" #{slot.Tag.Value}";
        }

        return name;
    }

    private void UpdateSlotIcon(Slot slot, TreeItem item)
    {
        // Set different icons based on slot state
        // This could be extended with actual icon resources

        if (!slot.ActiveSelf.Value)
        {
            item.SetCustomColor(0, Colors.Gray);
        }
        else if (slot.Components.Count > 0)
        {
            item.SetCustomColor(0, Colors.White);
        }
    }

    private void OnItemSelected()
    {
        var selected = _tree.GetSelected();
        if (selected == null) return;

        if (selected.HasMeta("slot"))
        {
            var slot = selected.GetMeta("slot").As<Slot>();
            _selectedSlot = slot;
            EmitSignal(SignalName.SlotSelected, slot);
        }
    }

    private void OnItemCollapsed(TreeItem item)
    {
        // Store collapsed state per slot for persistence
        if (item.HasMeta("slot"))
        {
            var slot = item.GetMeta("slot").As<Slot>();
            // Could store this in slot metadata
        }
    }

    private void OnSlotAdded(Slot slot)
    {
        // Refresh the parent's tree item
        if (slot.Parent != null && _slotToTreeItem.TryGetValue(slot.Parent, out var parentItem))
        {
            // Add new item under parent
            BuildTree(slot, parentItem);
        }
        else
        {
            // Root slot added, rebuild entire tree
            RefreshHierarchy();
        }
    }

    private void OnSlotRemoved(Slot slot)
    {
        if (_slotToTreeItem.TryGetValue(slot, out var item))
        {
            item.Free();
            _slotToTreeItem.Remove(slot);
        }
    }

    /// <summary>
    /// Expand all tree items to show full hierarchy.
    /// </summary>
    public void ExpandAll()
    {
        ExpandRecursive(_tree.GetRoot());
    }

    /// <summary>
    /// Collapse all tree items.
    /// </summary>
    public void CollapseAll()
    {
        CollapseRecursive(_tree.GetRoot());
    }

    private void ExpandRecursive(TreeItem item)
    {
        if (item == null) return;

        item.Collapsed = false;

        for (int i = 0; i < item.GetChildCount(); i++)
        {
            ExpandRecursive(item.GetChild(i));
        }
    }

    private void CollapseRecursive(TreeItem item)
    {
        if (item == null) return;

        item.Collapsed = true;

        for (int i = 0; i < item.GetChildCount(); i++)
        {
            CollapseRecursive(item.GetChild(i));
        }
    }

    /// <summary>
    /// Focus on a specific slot in the hierarchy.
    /// </summary>
    public void FocusSlot(Slot slot)
    {
        if (_slotToTreeItem.TryGetValue(slot, out var item))
        {
            // Expand parents
            var parent = item.GetParent();
            while (parent != null)
            {
                parent.Collapsed = false;
                parent = parent.GetParent();
            }

            // Select item
            item.Select(0);
            _tree.ScrollToItem(item);
        }
    }

    /// <summary>
    /// Get the currently selected slot.
    /// </summary>
    public Slot GetSelectedSlot() => _selectedSlot;
}
