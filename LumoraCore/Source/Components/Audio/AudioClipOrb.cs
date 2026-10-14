// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Physics;

namespace Lumora.Core.Components.Audio;

// A clip you can hold: a glass bead with a bright core and the clip's name under it. Hold it over a sound field
// on any component (a cue, a player, a footstep set) and let go, or press the field with it in hand, and the
// field takes the clip; the bead stays in the hand.
//
// It carries the clip and nothing else. Nothing plays from it, so a table of them is quiet; the player panel is
// the import for listening. The clip lives on the bead itself, so whatever was given it keeps pointing here:
// delete the bead and those fields lose the clip. -xlinka
[ComponentCategory("Audio")]
public sealed class AudioClipOrb : Component
{
    public const float Radius = 0.045f;

    private const float CoreRadius = 0.013f;
    private const float LabelSize = 0.016f;

    public IAssetProvider<AudioClip>? Clip => Slot?.GetComponent<StaticAudioClip>() is { IsDestroyed: false } clip ? clip : null;

    private Slot? _label;

    // The name turns to this machine's eyes. A bead is held up close to read it, and a billboard on your own body
    // faces where you face instead, which shows a bead in your hand its back.
    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        var head = world?.LocalUser?.Root?.HeadSlot;
        if (world == null || head == null || head.IsDestroyed || Slot == null)
            return;
        if (_label == null || _label.IsDestroyed)
            _label = Slot.FindChild("Label", recursive: false);
        var label = _label;
        if (label?.Parent == null || !Utility.UserFacing.TryLookRotation(label.GlobalPosition, head.GlobalPosition, float3.Up, yawOnly: true, out var facing))
            return;
        var local = label.Parent.GlobalRotation.Inverse * facing;
        float dot = floatQ.Dot(local, label.LocalRotation.Value);
        if (1f - MathF.Abs(dot) < 1e-6f)
            return;
        // Each machine turns it for its own viewer, so nothing is sent.
        using var bypass = world.DataModelPermissions?.EnterSystemBypass();
        label.LocalRotation.SetValueSilently(local, change: true);
    }

    public static AudioClipOrb Build(Slot slot, Uri clip, string label)
    {
        var root = slot.GetComponent<ObjectRoot>() ?? slot.AttachComponent<ObjectRoot>();
        if (string.IsNullOrEmpty(root.ObjectName.Value))
            root.ObjectName.Value = label;

        var provider = slot.AttachComponent<StaticAudioClip>();
        provider.URL.Value = clip;

        // The look hangs below the bead's own slot, so the only asset on the slot itself is the clip: holding
        // the bead over a mesh or material field hands those nothing.
        var beadSlot = slot.AddSlot("Bead");
        var bead = beadSlot.AttachComponent<SphereMesh>();
        bead.Radius.Value = Radius;
        bead.Segments.Value = 32;
        bead.Rings.Value = 16;
        var glass = beadSlot.AttachComponent<FresnelMaterial>();
        var rim = DashTheme.MeshTint(DashTheme.IconTeal);
        glass.FarColor.Value = new colorHDR(rim.r, rim.g, rim.b, 0.95f);
        glass.NearColor.Value = new colorHDR(rim.r, rim.g, rim.b, 0.08f);
        glass.BlendMode.Value = BlendMode.Alpha;
        var renderer = beadSlot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = bead;
        renderer.Material.Target = glass;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;

        var collider = slot.AttachComponent<SphereCollider>();
        collider.Radius.Value = Radius;
        collider.Type.Value = ColliderType.Trigger;

        var grab = slot.AttachComponent<Grabbable>();
        grab.AllowGrab.Value = true;
        grab.Scalable.Value = true;

        var coreSlot = slot.AddSlot("Core");
        var core = coreSlot.AttachComponent<SphereMesh>();
        core.Radius.Value = CoreRadius;
        core.Segments.Value = 16;
        core.Rings.Value = 8;
        var glow = coreSlot.AttachComponent<UnlitMaterial>();
        glow.TintColor.Value = DashTheme.MeshTint(DashTheme.IconTeal);
        var coreRenderer = coreSlot.AttachComponent<MeshRenderer>();
        coreRenderer.Mesh.Target = core;
        coreRenderer.Material.Target = glow;
        coreRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        var labelSlot = slot.AddSlot("Label");
        labelSlot.LocalPosition.Value = new float3(0f, -(Radius + LabelSize * 1.2f), 0f);
        // Text with no font draws nothing, so the bead brings its own.
        var font = labelSlot.AttachComponent<FontProvider>();
        font.URL.Value = DashTheme.FontSemibold;
        font.FallbackURLs.Add(DashTheme.FontRegular);
        var text = labelSlot.AttachComponent<TextRenderer>();
        text.Font.Target = font;
        text.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 0.85f);
        text.OutlineThickness.Value = 2f;
        text.Text.Value = label;
        text.Size.Value = LabelSize;
        text.Color.Value = color.White;
        text.HorizontalAlign.Value = Helio.UI.TextHorizontalAlignment.Center;
        text.VerticalAlign.Value = Helio.UI.TextVerticalAlignment.Top;

        return slot.AttachComponent<AudioClipOrb>();
    }
}
