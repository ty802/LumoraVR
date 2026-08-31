// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// "What colour is that thing" for the eyedropper. Two tiers, datamodel first.
//
// Tier one asks the DATAMODEL, walking up from the slot the laser hit. On each slot the renderer's
// material wins over anything else on the same slot: a lit prop carrying both a MeshRenderer and a
// Light is a coloured object standing next to its own lamp, and the object is what you were pointing
// at. Nothing on this slot answers, so try the parent - a hit lands on the child slot a mesh happens
// to live on far more often than on the slot somebody authored the material on.
//
// Tier two asks the SCREEN. It only runs when nothing in the datamodel can name a colour (a skybox, a
// particle, a texel of an imported texture), and it goes through the platform's view sampler, which is
// null on anything with no view at all. -xlinka
public static class ColorSampling
{
    // How far up the parent chain a hit is allowed to look for a colour. A hit on a stray empty slot
    // inside a big rig should find the thing it belongs to; it should not climb all the way to the
    // world root and report the skybox material somebody parked there. -xlinka
    private const int MaxParentWalk = 16;

    // Test seam: harnesses cannot reach InputInterface (no engine headless). Null in every session.
    public static IViewColorSampler? ViewSamplerOverride;

    public static bool TrySampleSlot(Slot? hitSlot, out colorHDR color)
        => TrySampleSlot(hitSlot, out color, out _);

    public static bool TrySampleSlot(Slot? hitSlot, out colorHDR color, out bool textured)
    {
        var current = hitSlot;
        for (int depth = 0; current != null && depth < MaxParentWalk; depth++, current = current.Parent)
        {
            if (current.IsDestroyed)
            {
                break;
            }
            if (TrySampleRenderers(current, out color, out textured) || TrySampleComponents(current, out color, out textured))
            {
                return true;
            }
        }

        color = colorHDR.White;
        textured = false;
        return false;
    }

    // The pixel the local view is showing at that point. Fails - honestly - with no sampler wired, no
    // camera, a point off screen or behind the eye, or a readback the platform refused.
    public static bool TrySampleView(float3 worldPoint, out colorHDR color)
    {
        var sampler = ViewSamplerOverride ?? Engine.Current?.InputInterface?.ViewColorSampler;
        if (sampler != null && sampler.TrySample(worldPoint, out color))
        {
            return true;
        }

        color = colorHDR.White;
        return false;
    }

    // A flat authored colour wins only while it is the truth. A textured material is usually white
    // albedo under the texture, so there the screen read leads and the flat colour is only the
    // fallback when no view can answer (headless, off screen). -xlinka
    public static bool TrySample(Slot? hitSlot, float3 worldPoint, out colorHDR color)
    {
        bool hasFlat = TrySampleSlot(hitSlot, out var flat, out bool textured);
        if (hasFlat && !textured)
        {
            color = flat;
            return true;
        }
        if (TrySampleView(worldPoint, out color))
        {
            return true;
        }
        if (hasFlat)
        {
            color = flat;
            return true;
        }
        return false;
    }

    private static bool TrySampleRenderers(Slot slot, out colorHDR color, out bool textured)
    {
        foreach (var renderer in slot.GetComponents<MeshRenderer>())
        {
            if (!IsUsable(renderer))
            {
                continue;
            }
            // Index the list rather than touching MeshRenderer.Material: that accessor ADDS an empty
            // slot-zero entry when the list is empty, and a sampler must not edit the thing it reads.
            for (int i = 0; i < renderer.Materials.Count; i++)
            {
                if (TryProvider(renderer.Materials[i], out color, out textured))
                {
                    return true;
                }
            }
        }

        foreach (var skinned in slot.GetComponents<SkinnedMeshRenderer>())
        {
            if (IsUsable(skinned) && TryProvider(skinned.Material.Target, out color, out textured))
            {
                return true;
            }
        }

        color = colorHDR.White;
        textured = false;
        return false;
    }

    private static bool TrySampleComponents(Slot slot, out colorHDR color, out bool textured)
    {
        textured = false;
        foreach (var source in slot.GetComponentsImplementing<IPrimaryColorSource>())
        {
            if (source is Component component && !IsUsable(component))
            {
                continue;
            }
            if (source.TryGetPrimaryColor(out color))
            {
                return true;
            }
        }

        color = colorHDR.White;
        return false;
    }

    private static bool TryProvider(object? provider, out colorHDR color, out bool textured)
    {
        if (provider is IPrimaryColorSource source
            && (provider is not Component component || IsUsable(component))
            && source.TryGetPrimaryColor(out color))
        {
            textured = provider is Lumora.Core.Assets.MaterialProvider material && material.PrimaryColorIsTextured;
            return true;
        }

        color = colorHDR.White;
        textured = false;
        return false;
    }

    private static bool IsUsable(Component component)
        => !component.IsDestroyed && component.Enabled.Value;
}
