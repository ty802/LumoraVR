// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class ColorDriver : Component
{
    public readonly SyncRef<InteractionElement> Interaction;

    // The tinted field AND the drive on it are the same member: pointing this at a color field is what
    // establishes the drive, on every peer and after a load. There is no separate local drive object to
    // re-derive, and no way for the ref and the drive to disagree about what is being tinted. -xlinka
    public readonly FieldDrive<color> Target;
    public readonly Sync<InteractionColorMode> TintColorMode;
    public readonly Sync<color> NormalColor;
    public readonly Sync<color> HighlightColor;
    public readonly Sync<color> PressedColor;
    public readonly Sync<color> DisabledColor;

    public ColorDriver()
    {
        Interaction = new SyncRef<InteractionElement>(this);
        Target = new FieldDrive<color>(this);
        TintColorMode = new Sync<InteractionColorMode>(this, InteractionColorMode.Explicit);
        NormalColor = new Sync<color>(this, color.White);
        HighlightColor = new Sync<color>(this, color.Lerp(color.White, color.Yellow, 0.2f));
        PressedColor = new Sync<color>(this, color.Lerp(color.White, new color(1f, 0.75f, 0f, 1f), 0.4f));
        DisabledColor = new Sync<color>(this, new color(0.65f));
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
        Target.SetValue(GetColor(interaction.CurrentInteractionState, interaction.BaseColor.Value));
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
