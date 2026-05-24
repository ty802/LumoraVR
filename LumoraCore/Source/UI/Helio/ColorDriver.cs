// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class ColorDriver : Component
{
    public readonly SyncRef<InteractionElement> Interaction;
    public readonly SyncRef<IField<color>> Target;
    public readonly Sync<InteractionColorMode> TintColorMode;
    public readonly Sync<color> NormalColor;
    public readonly Sync<color> HighlightColor;
    public readonly Sync<color> PressedColor;
    public readonly Sync<color> DisabledColor;

    private FieldDrive<color>? _drive;
    private IField<color>? _linkedTarget;

    public ColorDriver()
    {
        Interaction = new SyncRef<InteractionElement>(this);
        Target = new SyncRef<IField<color>>(this);
        TintColorMode = new Sync<InteractionColorMode>(this, InteractionColorMode.Explicit);
        NormalColor = new Sync<color>(this, color.White);
        HighlightColor = new Sync<color>(this, color.Lerp(color.White, color.Yellow, 0.2f));
        PressedColor = new Sync<color>(this, color.Lerp(color.White, new color(1f, 0.75f, 0f, 1f), 0.4f));
        DisabledColor = new Sync<color>(this, new color(0.65f));
    }

    public override void OnAwake()
    {
        base.OnAwake();
        _drive = new FieldDrive<color>(World);
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Interaction.Target == null)
        {
            Interaction.Target = Slot.GetComponent<InteractionElement>();
        }
        Apply();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Apply();
    }

    public override void OnDestroy()
    {
        _drive?.Release();
        _drive = null;
        _linkedTarget = null;
        base.OnDestroy();
    }

    public void SetColors(in color value)
    {
        NormalColor.Value = value;

        var (h, s, v) = value.ToHSV();
        bool saturatePress = s >= 0.1f;
        float pressSaturation = saturatePress ? Clamp01(s + 0.2f) : s;

        if (v < 0.5f)
        {
            HighlightColor.Value = color.FromHSV(h, s, Clamp01(v + 0.25f), value.a);
            PressedColor.Value = color.FromHSV(h, pressSaturation, Clamp01(v + 0.5f), value.a);
            DisabledColor.Value = new color(0.45f, value.a);
        }
        else
        {
            HighlightColor.Value = color.FromHSV(h, s, Clamp01(v - 0.25f), value.a);
            PressedColor.Value = color.FromHSV(h, pressSaturation, Clamp01(v - 0.5f), value.a);
            DisabledColor.Value = new color(0.65f, value.a);
        }
    }

    public void Apply()
    {
        var interaction = Interaction.Target ?? Slot?.GetComponent<InteractionElement>();
        if (interaction != null)
        {
            Apply(interaction);
        }
    }

    public void Apply(InteractionElement interaction)
    {
        if (!EnsureDrive())
        {
            return;
        }

        _drive!.SetValue(GetColor(interaction.CurrentInteractionState, interaction.BaseColor.Value));
    }

    private bool EnsureDrive()
    {
        if (_drive == null)
        {
            return false;
        }

        var target = Target.Target;
        if (ReferenceEquals(target, _linkedTarget))
        {
            return _drive.IsLinkValid;
        }

        _drive.ReleaseLink();
        _linkedTarget = null;

        if (target == null)
        {
            return false;
        }

        _drive.DriveTarget(target);
        _linkedTarget = target;
        return _drive.IsLinkValid;
    }

    private color GetColor(InteractionState state, in color baseColor)
    {
        var value = state switch
        {
            InteractionState.Disabled => DisabledColor.Value,
            InteractionState.Pressed => PressedColor.Value,
            InteractionState.Highlight => HighlightColor.Value,
            _ => NormalColor.Value,
        };

        return TintColorMode.Value switch
        {
            InteractionColorMode.Additive => baseColor + value,
            InteractionColorMode.Multiply => baseColor * value,
            InteractionColorMode.Direct => value,
            _ => baseColor * value,
        };
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }
}
