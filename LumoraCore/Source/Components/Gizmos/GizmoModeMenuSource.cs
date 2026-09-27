// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Gizmos;

// The developer tool's actions on the root ring: the selection's gizmo modes behind one Gizmo item, the
// lighting and probe setups, and clearing the selection. Create New and Inspector come from their own
// sources.
[ComponentCategory("Utility/Gizmos")]
public class GizmoModeMenuSource : ContextMenuItemSource
{
    public readonly SyncRef<Interaction.DevToolItem> Tool;

    private static readonly float[] GizmoFill = { 0.20f, 0.17f, 0.30f, 0.92f };
    private static readonly float[] ItemFill = { 0.16f, 0.15f, 0.24f, 0.92f };
    private static readonly float[] ActiveFill = { 0.35f, 0.28f, 0.62f, 0.95f };
    private static readonly float[] ClearFill = { 0.42f, 0.16f, 0.18f, 0.94f };

    public GizmoModeMenuSource()
    {
        Tool = new SyncRef<Interaction.DevToolItem>(this);
    }

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        // A user with only component gizmos still needs the selection utilities.
        var gizmo = ResolveGizmo();
        if (gizmo == null && !GizmoHelper.HasLocalGizmos(World) && GizmoHelper.AnyGizmo(World) == null)
            return;

        // Equipped in the other hand, the tool's actions are on that hand's ring.
        if (context?.Side != null && Tool.Target is { IsEquipped: true, ActiveTool: Interaction.HandTool hand } && hand.Side.Value != context.Side)
            return;

        int first = page.Items.Count;
        var modes = new ContextMenuPage("Gizmo");
        if (gizmo != null && EngineSettings.GizmoControls == EngineSettings.GizmoControlStyle.ContextMenu)
        {
            int mode = gizmo.ActiveMode.Value;
            modes.AddItem(new ContextMenuItem
            {
                Label = "Translate",
                FillColor = mode == 0 ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!gizmo.IsDestroyed) gizmo.SwitchToTranslation(); },
            });
            modes.AddItem(new ContextMenuItem
            {
                Label = "Rotate",
                FillColor = mode == 1 ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!gizmo.IsDestroyed) gizmo.SwitchToRotation(); },
            });
            modes.AddItem(new ContextMenuItem
            {
                Label = "Scale",
                FillColor = mode == 2 ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!gizmo.IsDestroyed) gizmo.SwitchToScale(); },
            });
        }
        // Cubes include their own axes button. The combined gizmo still needs a local/world switch.
        if (gizmo != null && EngineSettings.GizmoControls != EngineSettings.GizmoControlStyle.Cubes)
        {
            modes.AddItem(new ContextMenuItem
            {
                Label = gizmo.IsLocalSpace.Value ? "Space: Local" : "Space: Global",
                FillColor = ItemFill,
                OnPressed = _ => { if (!gizmo.IsDestroyed) gizmo.ToggleSpace(); },
            });
        }
        if (modes.Items.Count > 0)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Gizmo",
                FillColor = GizmoFill,
                SubPage = modes,
            });
        }

        page.AddItem(new ContextMenuItem
        {
            Label = "Reflection Probes",
            FillColor = ItemFill,
            OnPressed = _ => OpenProbeWizard(),
        });
        page.AddItem(new ContextMenuItem
        {
            Label = "Lighting",
            FillColor = ItemFill,
            OnPressed = _ => OpenLightingWizard(),
        });

        // Deselect Local only appears when there is something of YOURS to clear; without that check the
        // item sits there doing nothing while somebody else's selection is the only one in the room.
        var tool = Tool.Target;
        if (GizmoHelper.HasLocalGizmos(World))
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Deselect Local",
                FillColor = ClearFill,
                OnPressed = _ =>
                {
                    GizmoHelper.DeselectLocal(World);
                    if (tool is { IsDestroyed: false })
                        tool.ClearSelection();
                },
            });
        }

        page.AddItem(new ContextMenuItem
        {
            Label = "Deselect All",
            FillColor = ClearFill,
            OnPressed = _ =>
            {
                GizmoHelper.DeselectAll(World);
                if (tool is { IsDestroyed: false })
                    tool.ClearSelection();
            },
        });

        for (int i = first; i < page.Items.Count; i++)
            page.Items[i].Group = ContextMenuGroup.Tool;
    }

    // The tool's own selection wins; otherwise fall back to any live gizmo (the inspector spawns one
    // on tree selection), so the menu works no matter which flow put the gizmo up.
    private SlotGizmo? ResolveGizmo()
    {
        var selected = Tool.Target?.SelectedSlot.Target;
        if (selected != null && !selected.IsDestroyed)
        {
            var owned = GizmoHelper.GetGizmo(selected);
            if (owned != null && !owned.IsDestroyed)
                return owned;
        }
        return GizmoHelper.AnyGizmo(World);
    }

    private void OpenProbeWizard()
    {
        var world = World;
        if (world?.RootSlot == null)
            return;

        var existing = world.RootSlot.GetComponentInChildren<ReflectionProbeWizard>();
        if (existing != null && !existing.IsDestroyed)
        {
            existing.Slot.Destroy();
            return;
        }

        ReflectionProbeWizard.Spawn(world);
    }

    private void OpenLightingWizard()
    {
        var world = World;
        if (world?.RootSlot == null)
            return;

        var existing = world.RootSlot.GetComponentInChildren<LightingWizard>();
        if (existing != null && !existing.IsDestroyed)
        {
            existing.Slot.Destroy();
            return;
        }

        LightingWizard.Spawn(world);
    }
}
