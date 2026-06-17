// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard "Inventory" screen: lists saved items (grabbed objects saved via "Save to Inventory")
/// and spawns them back into the focused world in front of the user. Backed by <see cref="Inventory"/>.
/// </summary>
public sealed class InventoryScreen : WidgetScreen
{
    private static readonly color SpawnFill = new color(0.28f, 0.60f, 0.40f, 0.95f);
    private static readonly color DeleteFill = new color(0.70f, 0.24f, 0.28f, 0.95f);

    protected override float RowHeight => 38f;

    private Slot? _listRoot;

    protected override void OnShow()
    {
        base.OnShow();
        RebuildList();
    }

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();

        var root = builder.Current;
        var col = root.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 5f;
        col.PaddingLeft.Value = 16f;
        col.PaddingRight.Value = 16f;
        col.PaddingTop.Value = 16f;
        col.PaddingBottom.Value = 16f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        _listRoot = root;
        RebuildList();
    }

    private void RebuildList()
    {
        var root = _listRoot;
        if (root == null)
            return;
        root.DestroyChildren();

        var items = Inventory.ListItems();

        var header = BeginRow(root, "Header");
        var hb = RowBuilder(header);
        hb.MinWidth(200f).FlexibleWidth(1f);
        AddRowLabel(hb, $"Inventory ({items.Count})", 18f, SectionTitleColor, TextHorizontalAlignment.Left);
        AddInlineButton(header, "Refresh", TabFill, 80f, RebuildList);

        if (items.Count == 0)
            AddInfoRow(root, "Empty. Grab an object and pick \"Save to Inventory\" from its menu.");

        foreach (var path in items)
            ItemRow(root, path);

        MarkDirty();
    }

    private void ItemRow(Slot parent, string path)
    {
        var row = BeginRow(parent, "Item");
        var b = RowBuilder(row);

        b.MinWidth(150f).FlexibleWidth(1f);
        AddRowLabel(b, Inventory.DisplayName(path), 16f, TextPrimary, TextHorizontalAlignment.Left);

        AddInlineButton(row, "Spawn", SpawnFill, 92f, () => SpawnItem(path));
        AddInlineButton(row, "Delete", DeleteFill, 84f, () =>
        {
            Inventory.DeleteItem(path);
            RebuildList();
        });
    }

    private void SpawnItem(string path)
    {
        var world = Lumora.Core.Engine.Current?.WorldManager?.FocusedWorld;
        if (world == null)
            return;

        // A meter in front of the local user's head, or a sensible default if there's no head yet.
        var userRoot = world.LocalUser?.Root;
        float3 position = userRoot?.HeadSlot != null
            ? userRoot.HeadPosition + userRoot.HeadRotation * (float3.Backward * 1.0f)
            : new float3(0f, 1f, 0f);

        Inventory.SpawnItem(world, path, position);
    }

    private void AddInfoRow(Slot parent, string text)
    {
        var row = BeginRow(parent, "Info");
        var label = AddFillLabel(row, text, 15f, TextDim);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }
}
