// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Helio.UI;

namespace Lumora.Core.Components.Avatar;

// One switch in an avatar's menu. Path is where it sits ("Clothing/Hat": a Clothing folder holding Hat),
// State is the switch, and whatever it changes points a driver at State (a ValueSwitchDriver for objects
// and sizes, a BlendShapeSwitchDriver for shapes, a MaterialSwitchDriver for materials).
//
// Turning on a switch with an ExclusiveGroup turns the rest of that group off, so one outfit replaces
// another instead of piling on top of it. The state is saved with the avatar and replicated from the
// wearer like any other field, unless Saved is off: then the avatar always comes back with Default.
//
// A momentary switch is a button: on while it is held down and off again when let go, but on for a
// moment at least, so a quick tap still reaches everything that reads it. -xlinka
[ComponentCategory("Users/Avatar")]
public class AvatarToggle : Component
{
    public const float MomentaryMinimum = 0.3f;

    public readonly Sync<string> Path;

    public readonly Sync<bool> State;

    public readonly Sync<string> ExclusiveGroup;

    public readonly Sync<bool> Momentary;

    public readonly Sync<bool> Default;

    public readonly Sync<bool> Saved;

    public readonly AssetRef<TextureAsset> Icon;

    private Button? _heldBy;
    private double _pressedAt;
    private bool _releasing;

    public AvatarToggle()
    {
        Path = new Sync<string>(this, "");
        State = new Sync<bool>(this, false);
        ExclusiveGroup = new Sync<string>(this, "");
        Momentary = new Sync<bool>(this, false);
        Default = new Sync<bool>(this, false);
        Saved = new Sync<bool>(this, true);
        Icon = new AssetRef<TextureAsset>(this);
    }

    public override void Load(Persistence.DataTreeNode node, Persistence.LoadControl control)
    {
        base.Load(node, control);
        if (!Saved.Value)
            State.Value = Default.Value;
    }

    public string Label => LabelOf(Path.Value, Slot);

    public string[] Folders => FoldersOf(Path.Value);

    // The last part of a menu path, or the slot's name when the path ends without one.
    public static string LabelOf(string? path, Slot? slot)
    {
        path ??= "";
        int slash = path.LastIndexOf('/');
        var label = slash >= 0 ? path[(slash + 1)..] : path;
        return label.Length > 0 ? label : slot?.SlotName.Value ?? "";
    }

    // The folders a menu path files its control under, outermost first.
    public static string[] FoldersOf(string? path)
    {
        path ??= "";
        int slash = path.LastIndexOf('/');
        return slash > 0 ? path[..slash].Split('/', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
    }

    public void Set(bool on)
    {
        State.Value = on;
        var group = ExclusiveGroup.Value;
        if (!on || string.IsNullOrEmpty(group))
            return;
        var scope = Slot?.GetComponentInParents<AvatarForm>()?.Slot ?? Slot?.Parent;
        if (scope == null)
            return;
        foreach (var other in scope.GetComponentsInChildren<AvatarToggle>())
        {
            if (!ReferenceEquals(other, this) && other.ExclusiveGroup.Value == group && other.State.Value)
                other.State.Value = false;
        }
    }

    // A button pressed through this plate: on now, off once the plate is let go and the minimum has passed.
    // Without a plate (a flick, a script) it is a tap.
    public void Press(Button? by)
    {
        Set(true);
        _heldBy = by;
        _pressedAt = World?.Time.TotalTime ?? 0;
        if (_releasing)
            return;
        _releasing = true;
        StartTask(async () =>
        {
            try
            {
                while (!IsDestroyed)
                {
                    await WorldContext.NextUpdate();
                    bool held = _heldBy is { IsDestroyed: false } button && button.IsPressed.Value;
                    if (!held && (World?.Time.TotalTime ?? 0) - _pressedAt >= MomentaryMinimum)
                        break;
                }
                if (!IsDestroyed)
                    State.Value = false;
            }
            finally
            {
                _releasing = false;
                _heldBy = null;
            }
        });
    }
}
