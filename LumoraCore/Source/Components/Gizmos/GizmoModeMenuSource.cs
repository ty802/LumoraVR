// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Gizmos;

// The developer tool's Gizmo item on the root ring: the selection's gizmo modes and clearing the
// selection, behind one wedge. Create New and Inspector come from their own sources and go before it; the
// lighting and probe setups live in Create New's Editor folder.
[ComponentCategory("Utility/Gizmos")]
public class GizmoModeMenuSource : ContextMenuItemSource
{
    public readonly SyncRef<Interaction.DevToolItem> Tool;

    // After the Create New and Inspector sources, which are the tool's first two items.
    public const int RingPriority = -10;

    // Older sessions can already contain several sources for the same tool. Collapse those while
    // keeping the two hands separate, so an ineligible source cannot hide the other hand's entry.
    public override string? SingleItemKey => Tool.Target is { } tool ? $"Gizmo/{tool.ReferenceID}" : null;
    public override bool KeepFirstSingleItem => true;

    private static readonly float[] GizmoFill = { 0.20f, 0.17f, 0.30f, 0.92f };
    private static readonly float[] ItemFill = { 0.16f, 0.15f, 0.24f, 0.92f };
    private static readonly float[] ClearFill = { 0.42f, 0.16f, 0.18f, 0.94f };

    public GizmoModeMenuSource()
    {
        Tool = new SyncRef<Interaction.DevToolItem>(this);
    }

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        if (Tool.Target is not { ActiveTool: Interaction.HandTool hand } tool || !tool.IsEquippedInHand(hand))
            return;
        // A carried spare or another user's DevTip must not contribute to this hand's ring.
        if (context?.Side != null && hand.Side.Value != context.Side)
            return;
        if (context?.Pointer is { } pointer && !ReferenceEquals(pointer, hand.Laser?.Slot))
            return;

        int first = page.Items.Count;
        var gizmos = GizmoPage();
        if (gizmos.Items.Count > 0)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Gizmo",
                FillColor = GizmoFill,
                SubPage = gizmos,
            });
        }

        for (int i = first; i < page.Items.Count; i++)
            page.Items[i].Group = ContextMenuGroup.Tool;
    }

    // The selection's gizmo: its modes, its space, and clearing the selection. Empty while nothing is
    // selected anywhere in the world.
    private ContextMenuPage GizmoPage()
    {
        var modes = new ContextMenuPage("Gizmo");
        var gizmo = ResolveGizmo();
        if (gizmo == null && !GizmoHelper.HasLocalGizmos(World) && GizmoHelper.AnyGizmo(World) == null)
            return modes;

        if (gizmo != null && EngineSettings.GizmoControls == EngineSettings.GizmoControlStyle.ContextMenu)
        {
            modes.AddItem(ModeItem(gizmo, "Translate", 0, g => g.SwitchToTranslation()));
            modes.AddItem(ModeItem(gizmo, "Rotate", 1, g => g.SwitchToRotation()));
            modes.AddItem(ModeItem(gizmo, "Scale", 2, g => g.SwitchToScale()));
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

        // Deselect Mine only when there is something of YOURS to clear; without that check the item sits
        // there doing nothing while somebody else's selection is the only one in the room.
        var tool = Tool.Target;
        if (GizmoHelper.HasLocalGizmos(World))
        {
            modes.AddItem(new ContextMenuItem
            {
                Label = "Deselect Mine",
                FillColor = ClearFill,
                OnPressed = _ =>
                {
                    GizmoHelper.DeselectLocal(World);
                    if (tool is { IsDestroyed: false })
                        tool.ClearSelection();
                },
            });
        }
        modes.AddItem(new ContextMenuItem
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
        return modes;
    }

    // One of the gizmo's modes, lit while it is the one in use. The ring stays up, so a switch shows on the
    // plates straight away and another needs no reopen.
    private static ContextMenuItem ModeItem(SlotGizmo gizmo, string label, int mode, System.Action<SlotGizmo> switchTo)
    {
        return new ContextMenuItem
        {
            Label = label,
            FillColor = ItemFill,
            IsToggle = true,
            IsToggled = gizmo.ActiveMode.Value == mode,
            KeepsOpen = true,
            OnPressed = _ => { if (!gizmo.IsDestroyed) switchTo(gizmo); },
            Refresh = item =>
            {
                bool on = !gizmo.IsDestroyed && gizmo.ActiveMode.Value == mode;
                if (item.IsToggled == on)
                    return false;
                item.IsToggled = on;
                return true;
            },
        };
    }

    // The tool's own selection wins; otherwise use a local selection from the inspector. Another
    // user's selection must not silently become the target of this hand's mode controls.
    private SlotGizmo? ResolveGizmo()
    {
        var selected = Tool.Target?.SelectedSlot.Target;
        if (selected != null && !selected.IsDestroyed)
        {
            var owned = GizmoHelper.GetGizmo(selected);
            if (owned != null && !owned.IsDestroyed)
                return owned;
        }
        var local = WorldGizmos.For(World)?.OwnedBy(World?.LocalUser);
        return local is { Count: > 0 } ? local[0] : null;
    }
}
