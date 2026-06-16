// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard "Worlds" screen: a world/session browser. Lists the worlds you currently have open
/// and lets you switch focus between them; switching focus re-points the Session tab. Creating a
/// new world lives on the Home screen as a widget, not here. (Remote session discovery — public /
/// LAN listings — is a later addition; for now this browses the worlds this client holds.)
/// </summary>
public sealed class WorldsScreen : WidgetScreen
{
    private static readonly color EnterFill = new color(0.28f, 0.50f, 0.72f, 0.95f);

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

        var header = BeginRow(root, "Header");
        var hb = RowBuilder(header);
        hb.MinWidth(200f).FlexibleWidth(1f);
        AddRowLabel(hb, "Worlds", 18f, SectionTitleColor, TextHorizontalAlignment.Left);
        AddInlineButton(header, "Refresh", TabFill, 80f, RebuildList);

        var manager = Lumora.Core.Engine.Current?.WorldManager;
        if (manager == null)
        {
            AddInfoRow(root, "No world manager available.");
            return;
        }

        var userspace = manager.UserspaceWorld;
        var focused = manager.FocusedWorld;
        int shown = 0;
        foreach (var world in manager.Worlds)
        {
            if (world == null || ReferenceEquals(world, userspace))
                continue;
            WorldRow(root, manager, world, ReferenceEquals(world, focused));
            shown++;
        }

        if (shown == 0)
            AddInfoRow(root, "No open worlds. Create one from Home → Create New World.");

        MarkDirty();
    }

    private void WorldRow(Slot parent, Management.WorldManager manager, World world, bool isFocused)
    {
        var row = BeginRow(parent, "World");
        var b = RowBuilder(row);

        var name = world.WorldName?.Value;
        if (string.IsNullOrEmpty(name))
            name = world.Name;

        b.MinWidth(150f).FlexibleWidth(1f);
        AddRowLabel(b, name, 16f, TextPrimary, TextHorizontalAlignment.Left);

        int users = world.GetAllUsers().Count;
        b.MinWidth(80f).PreferredWidth(80f).FlexibleWidth(0f);
        AddRowLabel(b, users == 1 ? "1 user" : $"{users} users", 14f, TextDim, TextHorizontalAlignment.Right);

        if (isFocused)
        {
            var cell = row.AddSlot("Focused");
            cell.AttachComponent<RectTransform>();
            var element = cell.AttachComponent<LayoutElement>();
            element.MinWidth.Value = 92f;
            element.PreferredWidth.Value = 92f;
            element.FlexibleWidth.Value = 0f;
            element.FlexibleHeight.Value = 1f;
            ApplyRoundedPanel(cell, AccentColor, RowBorder);
            AddFillLabel(cell, "Focused", 14f, TextPrimary);
        }
        else
        {
            AddInlineButton(row, "Enter", EnterFill, 92f, () =>
            {
                manager.SwitchToWorld(world);
                RebuildList();
            });
        }
    }

    private void AddInfoRow(Slot parent, string text)
    {
        var row = BeginRow(parent, "Info");
        var label = AddFillLabel(row, text, 15f, TextDim);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }
}
