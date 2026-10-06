// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Opens the colour picker on a field when the button on this slot is pressed, between the press and the
// person pressing; a second press closes it again. The picker edits the field live, the way it does from an
// inspector, so whatever reads the field follows along. Each machine keeps its own picker. -xlinka
[ComponentCategory("Utility/Buttons")]
public sealed class PressEditColor : Component, IPressReceiver
{
    // A color or colorHDR field.
    public readonly SyncRef<IField> Target;

    private ColorPickerPanel? _open;

    public PressEditColor()
    {
        Target = new SyncRef<IField>(this);
    }

    // A physical control is pressed on the authority; the picker belongs on the presser's own machine.
    public void OnPressed(in PressEvent press)
    {
        if (press.Actor != null && !ReferenceEquals(press.Actor, World?.LocalUser))
            return;
        if (_open is { IsDestroyed: false } open)
        {
            _open = null;
            if (open.Slot is { IsDestroyed: false } panel)
                panel.Destroy();
            return;
        }
        var field = Target.Target;
        var world = World;
        if (field == null || field.IsDestroyed || world == null
            || (field.ValueType != typeof(color) && field.ValueType != typeof(colorHDR)))
            return;
        var head = world.LocalUser?.Root?.HeadSlot;
        float3 point = press.Point;
        float3 offset = head != null && (head.GlobalPosition - point).LengthSquared > 1e-6f
            ? (head.GlobalPosition - point).Normalized * 0.3f
            : float3.Up * 0.1f;
        _open = ColorPickerPanel.Spawn(world, field, "", point + offset);
    }

    public override void OnDestroy()
    {
        _open = null;
        base.OnDestroy();
    }
}
