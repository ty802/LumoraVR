// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// The "Create New" browser: walk a category tree, press a leaf, get the thing in front of you.
//
// Structurally the same panel as the component selector, for the same reason it is built that way -
// rows carry their action in a SYNCED string argument rather than a closure, so a press works for
// every user rather than only the one whose client happened to build the row.
//
// The catalog itself lives in CreateNewCatalog; this is only the browser over it. -xlinka
[ComponentCategory("Utility/Inspectors")]
public sealed class CreateNewPanel : Component, IInspectorActionHandler
{
    public readonly Sync<string> CategoryPath;

    // Told what an entry made, on the machine whose press made it. A material tool converting through this
    // panel is what listens. Not synced: it is one user's errand. -xlinka
    public Action<Slot>? OnCreated;

    private readonly SyncRef<Slot> _listContent;
    private readonly Sync<string> _builtPath;

    private const float RowHeight = InspectorUI.RowHeight;

    public CreateNewPanel()
    {
        CategoryPath = new Sync<string>(this, "");
        _listContent = new SyncRef<Slot>(this);
        _builtPath = new Sync<string>(this, "unbuilt");
    }

    // Placement copies the scene inspector's: a metre out along the head's flattened forward, a little
    // below eye line, then yawed back to face the user. Never LookRotation, which returns the inverse.
    public static CreateNewPanel Spawn(World world)
    {
        if (world?.RootSlot == null)
            return null!;

        using var own = world.EnterSpawnAllocation();
        var panelSlot = world.RootSlot.AddSlot("Create New");
        panelSlot.Persistent.Value = false;
        panelSlot.Tag.Value = "Developer";

        var head = world.LocalUser?.Root?.HeadSlot;
        if (head != null && !head.IsDestroyed)
        {
            float3 forward = head.GlobalRotation * float3.Backward;
            forward.y = 0f;
            forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;

            panelSlot.GlobalPosition = head.GlobalPosition + forward * 1.0f + new float3(0f, -0.12f, 0f);
            panelSlot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(-forward.x, -forward.z));
        }

        // Half a millimetre a pixel like the other inspector panels; without it the canvas came up a metre a
        // pixel.
        panelSlot.LocalScale.Value = float3.One * 0.0005f;
        return panelSlot.AttachComponent<CreateNewPanel>();
    }

    public override void OnAttach()
    {
        base.OnAttach();

        var theme = Slot.GetOrAttachComponent<UITheme>();

        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Create New";
        shell.Size.Value = new float2(520f, 900f);
        theme.ApplyTo(shell);
        shell.RebuildContent(BuildLayout);
    }

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        InspectorUI.ApplyTheme(ui, Slot);

        var vLayout = page.AttachComponent<VerticalLayout>();
        vLayout.Spacing.Value = 6f;
        vLayout.PaddingLeft.Value = 6f;
        vLayout.PaddingRight.Value = 6f;
        vLayout.PaddingTop.Value = 6f;
        vLayout.PaddingBottom.Value = 6f;
        vLayout.ForceExpandWidth.Value = true;
        vLayout.ForceExpandHeight.Value = false;

        var host = page.AddSlot("List");
        host.AttachComponent<RectTransform>();
        var hostLE = host.AttachComponent<LayoutElement>();
        hostLE.FlexibleHeight.Value = 1f;
        hostLE.MinHeight.Value = 200f;

        var scrollUi = new UIBuilder(host);
        InspectorUI.ApplyTheme(scrollUi, Slot);
        var scroll = scrollUi.ScrollRect(out var content, null, InspectorUI.PaneColor);
        scroll.RowsActOnRelease.Value = true;
        InspectorUI.FillParent(scroll.Slot.GetComponent<RectTransform>()!);

        var contentLayout = content.Slot.AttachComponent<VerticalLayout>();
        contentLayout.Spacing.Value = 2f;
        contentLayout.ForceExpandWidth.Value = true;
        contentLayout.ForceExpandHeight.Value = false;
        _listContent.Target = content.Slot;
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;
        if (_builtPath.Value == CategoryPath.Value)
            return;
        _builtPath.Value = CategoryPath.Value;
        RebuildList();
    }

    private void RebuildList()
    {
        var container = _listContent.Target;
        if (container == null || container.IsDestroyed)
            return;
        container.DestroyChildren();

        var node = CreateNewCatalog.GetNode(CategoryPath.Value) ?? CreateNewCatalog.Root;

        if (!string.IsNullOrEmpty(CategoryPath.Value))
            AddRow(container, "< Back", "back:", InspectorUI.MutedColor);

        foreach (var sub in node.Subcategories.Values)
            AddRow(container, sub.Name + " >", "cat:" + sub.Path, InspectorUI.AccentColor);

        // Index rather than name, so two entries could share a label without colliding. An entry that only
        // shows up through an inspector is left out where the world refuses inspectors.
        bool inspectors = World?.AllowsWorldEditing == true;
        for (int i = 0; i < node.Entries.Count; i++)
        {
            if (node.Entries[i].OpensInspector && !inspectors)
                continue;
            AddRow(container, node.Entries[i].Name, "make:" + i, InspectorUI.TextColor);
        }
    }

    private void AddRow(Slot container, string label, string action, color tint)
    {
        InspectorUI.FixedRow(container, label, RowHeight, out var ui, Slot);
        ui.PushStyle();
        ui.TextColor(tint);
        var button = InspectorUI.RelayButton(ui, this, action, label, 0f);
        ui.PopStyle();
    }

    public void HandleInspectorAction(string action)
    {
        if (action.StartsWith("back:", StringComparison.Ordinal))
        {
            var path = CategoryPath.Value ?? "";
            int slash = path.LastIndexOf('/');
            CategoryPath.Value = slash > 0 ? path[..slash] : "";
            return;
        }

        if (action.StartsWith("cat:", StringComparison.Ordinal))
        {
            CategoryPath.Value = action[4..];
            return;
        }

        if (action.StartsWith("make:", StringComparison.Ordinal))
        {
            if (!int.TryParse(action[5..], out int index))
                return;
            var made = Build(index, out var entry);
            if (made == null || entry == null)
                return;
            ShowResult(made, entry);
            CloseAfterUse();
        }
    }

    // An Editor entry hands back the slot it already dropped for its wizard, which counts as made.
    private Slot? Build(int index, out CreateNewCatalog.Entry? entry)
    {
        entry = null;
        var world = World;
        var node = CreateNewCatalog.GetNode(CategoryPath.Value) ?? CreateNewCatalog.Root;
        if (world?.RootSlot == null || index < 0 || index >= node.Entries.Count)
            return null;

        entry = node.Entries[index];
        using var own = world.EnterSpawnAllocation();
        var slot = world.RootSlot.AddSlot(entry.Name);
        if (slot == null)
            return null;

        // In front of the user at chest height, not at the world origin. Same head-relative math the
        // panel itself uses to place itself.
        var head = world.LocalUser?.Root?.HeadSlot;
        if (head != null && !head.IsDestroyed)
        {
            float3 forward = head.GlobalRotation * float3.Backward;
            forward.y = 0f;
            forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;
            slot.GlobalPosition = head.GlobalPosition + forward * 0.8f + new float3(0f, -0.3f, 0f);
        }

        try
        {
            entry.Build(slot);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"CreateNewPanel: '{entry.Name}' failed to build - {ex.Message}");
            slot.Destroy();
            return null;
        }

        // Spawning is an undoable action like any other creation, so it joins the same stack rather
        // than being the one way to add things to a world that cannot be taken back.
        InspectorUndo.Record(this, SlotExistenceUndoBatch.Created(world, new[] { slot }, UndoLocale.SpawnShape));
        OnCreated?.Invoke(slot);
        return slot;
    }

    // Beside the panel rather than on the inspector's usual spot, which is just behind the new object:
    // there the object's gizmo would take the presses meant for the inspector. Runs before the close, while
    // the panel's pose can still be read.
    private void ShowResult(Slot made, CreateNewCatalog.Entry entry)
    {
        if (!entry.OpensInspector || made.IsDestroyed || World == null || Slot == null || Slot.IsDestroyed)
            return;
        var position = Slot.GlobalPosition;
        var beside = position + Slot.GlobalRotation * new float3(0.55f, 0f, 0f);
        SceneInspectorPanel.OpenOrFocus(World, made, position, spawnAt: beside, mineOnly: true);
    }

    // Anyone can press the rows, but only someone allowed to remove the panel closes it. Anyone else is
    // refused and it stays up for whoever opened it.
    private void CloseAfterUse()
    {
        if (IsDestroyed || Slot == null || Slot.IsDestroyed)
            return;
        try
        {
            var shell = Slot.GetComponent<PanelShell>();
            if (shell != null)
                shell.Close();
            else
                Slot.Destroy();
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
