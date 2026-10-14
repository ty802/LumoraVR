// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Linq;
using Lumora.Core;
using Lumora.Core.Components.UI;

namespace Lumora.Core.Components.Avatar;

// The worn avatar's own switches on the context menu: one wedge on the root ring, its toggles and sliders
// inside, filed into folders by their paths, with the package's icons where it gave them. A slider is a value
// plate that opens a dial; a button acts while its plate is held. Only the person wearing it gets the wedge;
// an avatar held in the hand or standing in the world hangs under someone's root too, and its switches are
// not theirs to flip. -xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarMenu : ContextMenuItemSource
{
    public readonly Sync<string> Label;

    private static readonly float[] WedgeFill = { 0.18f, 0.20f, 0.32f, 0.92f };
    private static readonly float[] FolderFill = { 0.16f, 0.17f, 0.27f, 0.92f };
    private static readonly float[] OffFill = { 0.14f, 0.14f, 0.20f, 0.92f };
    private static readonly float[] OnFill = { 0.24f, 0.46f, 0.36f, 0.95f };

    public AvatarMenu()
    {
        Label = new Sync<string>(this, "Avatar");
    }

    public override void PopulateContextMenu(ContextMenuPage page, ContextMenuContext context)
    {
        var form = Slot?.GetComponentInParents<AvatarForm>();
        var wearer = form?.Wearer;
        if (wearer == null || World?.LocalUser == null || !ReferenceEquals(wearer.ActiveUser, World.LocalUser))
            return;

        var controls = form!.Slot.GetComponentsInChildren<Component>()
            .Where(c => c is AvatarToggle or AvatarSlider && c.Enabled.Value).ToList();
        if (controls.Count == 0)
            return;

        var label = string.IsNullOrEmpty(Label.Value) ? "Avatar" : Label.Value;
        var menu = new ContextMenuPage(label);
        foreach (var control in controls)
        {
            if (control is AvatarSlider slider)
            {
                AddSlider(menu, slider);
                continue;
            }
            var toggle = (AvatarToggle)control;
            var target = menu;
            foreach (var folder in toggle.Folders)
                target = target.GetOrAddSubPage(folder, FolderFill);
            bool on = toggle.State.Value;
            var picked = toggle;
            var plate = new ContextMenuItem
            {
                Label = toggle.Label,
                FillColor = on ? OnFill : OffFill,
                IconAsset = toggle.Icon.Target,
                IsToggle = true,
                IsToggled = on,
                KeepsOpen = true,
                OnPressed = _ => { if (!picked.IsDestroyed) picked.Set(!picked.State.Value); },
                Refresh = item =>
                {
                    if (picked.IsDestroyed || item.IsToggled == picked.State.Value)
                        return false;
                    item.IsToggled = picked.State.Value;
                    item.FillColor = item.IsToggled ? OnFill : OffFill;
                    return true;
                },
            };
            // A button is on while its plate is held.
            if (toggle.Momentary.Value)
                plate.OnButton = (_, button, _) => { if (!picked.IsDestroyed) picked.Press(button); };
            target.AddItem(plate);
        }

        page.AddItem(new ContextMenuItem
        {
            Label = label,
            FillColor = WedgeFill,
            SubPage = menu,
            Group = ContextMenuGroup.Self,
        });
    }

    private static void AddSlider(ContextMenuPage menu, AvatarSlider slider)
    {
        var target = menu;
        foreach (var folder in slider.Folders)
            target = target.GetOrAddSubPage(folder, FolderFill);
        target.AddItem(new ContextMenuItem
        {
            Label = slider.Label,
            FillColor = OffFill,
            IconAsset = slider.Icon.Target,
            ReadValue = () => slider.IsDestroyed ? 0f : slider.Value.Value,
            WriteValue = value => { if (!slider.IsDestroyed) slider.Set(value); },
            DefaultValue = slider.Default.Value,
            Refresh = item =>
            {
                bool usable = !slider.IsDestroyed && slider.Enabled.Value;
                if (item.IsEnabled == usable)
                    return false;
                item.IsEnabled = usable;
                return true;
            },
        });
    }
}
