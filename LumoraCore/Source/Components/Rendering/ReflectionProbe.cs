// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

public enum ProbeUpdateMode
{
    // then never again until something asks
    Once,

    // six scene renders per frame per probe - use it deliberately
    Always,
}

public enum ProbeAmbientMode
{
    Disabled,

    Environment,

    // for interiors the sky cannot see into
    Color,
}

// Default is ProbeUpdateMode.Once and that default is the whole cost story: a bake is
// six scene renders, so Always is six renders EVERY FRAME on top of the ordinary one. Anything that
// changes occasionally wants Once plus a Rebake when it changes; Always is for a
// mirror in a room where things are genuinely moving all the time, and even then only up close.
// -xlinka
[ComponentCategory("Rendering")]
public class ReflectionProbe : ImplementableComponent, ICustomInspectorUI
{
    // local units, centred on the slot
    public readonly Sync<float3> Size;

    // moves the capture point away from the box centre without moving the box
    public readonly Sync<float3> OriginOffset;

    // see ProbeUpdateMode before reaching for Always
    public readonly Sync<ProbeUpdateMode> UpdateMode;

    [Range(0f, 4f, "0.00")]
    public readonly Sync<float> Intensity;

    // reprojects the captured cubemap onto the box, so a reflection lands where the wall actually is
    // instead of at infinity; right for rooms and corridors, wrong for open ground
    public readonly Sync<bool> BoxProjection;

    // treats the box as an enclosed space: the sky stops leaking in, and AmbientMode takes over
    // ambient light inside it
    public readonly Sync<bool> Interior;

    [Range(0f, 10f, "0.00")]
    public readonly Sync<float> BlendDistance;

    // smaller is cheaper
    public readonly Sync<float> MaxDistance;

    // 0 keeps the renderer default
    public readonly Sync<int> CullMask;

    // roughly doubles what a bake costs
    public readonly Sync<bool> CaptureShadows;

    // only meaningful with Interior set
    [Group("Interior ambient")]
    public readonly Sync<ProbeAmbientMode> AmbientMode;

    public readonly Sync<color> AmbientColor;

    [Range(0f, 4f, "0.00")]
    public readonly Sync<float> AmbientEnergy;

    // bumped by Rebake; the hook watches this rather than a method call because a hook only ever sees
    // state, and a counter is the smallest piece of state that means "again"
    [HideInInspector]
    public readonly Sync<int> BakeGeneration;

    public ReflectionProbe()
    {
        Size = new Sync<float3>(this, new float3(10f, 10f, 10f));
        OriginOffset = new Sync<float3>(this, float3.Zero);
        UpdateMode = new Sync<ProbeUpdateMode>(this, ProbeUpdateMode.Once);
        Intensity = new Sync<float>(this, 1f);
        BoxProjection = new Sync<bool>(this, false);
        Interior = new Sync<bool>(this, false);
        BlendDistance = new Sync<float>(this, 1f);
        MaxDistance = new Sync<float>(this, 0f);
        CullMask = new Sync<int>(this, 0);
        CaptureShadows = new Sync<bool>(this, false);
        AmbientMode = new Sync<ProbeAmbientMode>(this, ProbeAmbientMode.Environment);
        AmbientColor = new Sync<color>(this, new color(0f, 0f, 0f, 1f));
        AmbientEnergy = new Sync<float>(this, 1f);
        BakeGeneration = new Sync<int>(this, 0);
    }

    // a Once probe holds the room as it looked when it first came up, so anything that moves the
    // furniture has to say so
    [SyncMethod]
    public void Rebake()
    {
        BakeGeneration.Value++;
    }

    // sizing a probe by hand is the fiddly part of using one, and the bounds walk that answers it
    // already exists for the inspector's gizmos
    public static ReflectionProbe? CreateForBounds(Slot slot, float padding = 0.5f)
    {
        if (slot == null || slot.IsDestroyed)
            return null;
        if (!SlotBoundsHelper.TryComputeWorldBounds(slot, out var bounds))
            return null;

        var probeSlot = slot.AddSlot("ReflectionProbe");
        probeSlot.GlobalPosition = bounds.Center;
        probeSlot.GlobalRotation = floatQ.Identity;

        var probe = probeSlot.AttachComponent<ReflectionProbe>();

        // The box is in the probe slot's local space, so a scaled parent would otherwise stretch it.
        var scale = probeSlot.GlobalScale;
        var size = bounds.Size + new float3(padding * 2f, padding * 2f, padding * 2f);
        probe.Size.Value = new float3(
            size.x / SafeScale(scale.x),
            size.y / SafeScale(scale.y),
            size.z / SafeScale(scale.z));
        return probe;
    }

    private static float SafeScale(float value)
    {
        float magnitude = value < 0f ? -value : value;
        return magnitude < 1e-4f ? 1f : magnitude;
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Probes", EngineSettings.ReflectionsEnabled
            ? "on"
            : "off in settings, this probe is not rendering");
        InspectorStats.AddRow(ui, "Face size", "fixed by the renderer's reflection atlas");
        InspectorStats.AddRow(ui, "Cost", UpdateMode.Value == ProbeUpdateMode.Always
            ? "six scene renders per frame"
            : $"six scene renders per bake, {BakeGeneration.Value} requested");
    }
}
