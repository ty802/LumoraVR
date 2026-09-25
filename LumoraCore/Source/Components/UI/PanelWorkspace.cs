// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Window controls for a world panel: pin to a user, lock in place, hide from scene cameras, and a frame
// colour for telling a pile of panels apart. The pin parents under the user's ROOT, not the head, so the
// panel rides locomotion without swinging across the view every time the head turns. -xlinka
[ComponentCategory("UI")]
public sealed class PanelWorkspace : Component
{
    public readonly SyncRef<User> PinnedUser = new();
    public readonly SyncRef<Slot> OriginalParent = new();
    public readonly Sync<bool> PositionLocked = new();
    public readonly Sync<bool> HideFromCamera = new();
    public readonly Sync<int> ColorIndex = new();

    public static readonly color[] Colors =
    {
        DashTheme.IconBlue, DashTheme.Warning, DashTheme.Positive,
        DashTheme.Accent, DashTheme.IconPink, DashTheme.IconTeal,
    };

    private color? _fallbackHeader, _fallbackBorder;

    public color? FrameColor => ColorIndex.Value >= 1 && ColorIndex.Value <= Colors.Length ? Colors[ColorIndex.Value - 1] : null;

    public override void OnStart() { base.OnStart(); Apply(); }
    public override void OnChanges() { base.OnChanges(); Apply(); }

    // Every peer runs this, so every write is equality-gated: agreeing peers write nothing and the
    // synced shell fields never ping-pong. -xlinka
    private void Apply()
    {
        var shell = Slot.GetComponent<PanelShell>();
        if (shell == null)
            return;
        Set(shell.AllowGrab, !PositionLocked.Value);

        var layer = Slot.GetComponent<RenderLayerOverride>();
        if (HideFromCamera.Value && layer == null)
            layer = Slot.AttachComponent<RenderLayerOverride>();
        // The hook only restamps nodes when its layer changes, so going back to visible is a write to the
        // public layer rather than a destroy that would leave every existing mesh on the eyes-only bit.
        if (layer != null)
            Set(layer.Layer, HideFromCamera.Value ? RenderLayerOverride.EyesOnlyLayer : 1);

        var theme = Slot.GetComponent<UITheme>();
        _fallbackHeader ??= shell.HeaderColor.Value;
        _fallbackBorder ??= shell.BorderColor.Value;
        color header = theme?.Header.Value ?? _fallbackHeader.Value;
        color border = theme?.Border.Value ?? _fallbackBorder.Value;
        if (FrameColor is { } tint)
        {
            header = Mix(header, tint, 0.3f);
            border = tint;
        }
        Set(shell.HeaderColor, header);
        Set(shell.BorderColor, border);
    }

    public void TogglePin(User? user)
    {
        if (PinnedUser.Target != null)
        {
            var parent = OriginalParent.Target;
            Slot.SetParent(parent is { IsDestroyed: false } && !parent.IsDescendantOf(Slot) ? parent : World.RootSlot, true);
            PinnedUser.Target = null!;
            OriginalParent.Target = null!;
        }
        else if (user?.Root?.Slot is { IsDestroyed: false } root && root.World == World && !root.IsDescendantOf(Slot))
        {
            PositionLocked.Value = false;
            OriginalParent.Target = Slot.Parent;
            Slot.SetParent(root, true);
            PinnedUser.Target = user;
        }
        Apply();
    }

    public void TogglePositionLock()
    {
        PositionLocked.Value = !PositionLocked.Value;
        if (PositionLocked.Value)
        {
            var grab = Slot.GetComponent<Grabbable>();
            if (grab?.Grabber != null) grab.Grabber.Release(grab);
            if (PinnedUser.Target != null || Slot.Parent != World.RootSlot)
                Slot.SetParent(World.RootSlot, true);
            PinnedUser.Target = null!;
            OriginalParent.Target = null!;
        }
        Apply();
    }

    public void CycleColor()
    {
        ColorIndex.Value = (ColorIndex.Value + 1) % (Colors.Length + 1);
        Apply();
    }

    private static color Mix(in color a, in color b, float t)
        => new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);

    private static void Set<T>(Sync<T> field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field.Value, value))
            field.Value = value;
    }
}
