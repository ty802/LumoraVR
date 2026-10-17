// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Interaction;

// Points out what your camera should not film. Primary on something leaves it out of the view and the
// photos of your camera in the world you are in, and it flashes so you see what went; secondary on it, or
// on any piece of it, puts it back. A press on a person leaves them out whole, body, plate and laser, and
// one on something they hold takes just that; anything else goes as the object the piece belongs to. The
// list is the camera's and every machine holds it, but the flash and the notes are drawn on the eyes-only
// band, so no camera ever films the tool at work. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class CameraLeaveOutTool : EquippableTool
{
    private const double FlashSeconds = 0.6;
    private const double NoticeSeconds = 2.5;
    private const float NoticeReach = 0.6f;
    // A flash is feedback, not a render of the whole thing; a huge object flashes its first parts.
    private const int FlashPartLimit = 128;

    private static readonly colorHDR FlashTint = new(0.2f, 0.95f, 1f, 1f);

    private Slot? _marks;
    private OverlayUnlitMaterial? _flashMaterial;
    private readonly List<(Slot Source, Slot Copy)> _flash = new();
    private double _flashStart;
    private Slot? _noticeSlot;
    private TextRenderer? _notice;
    private double _noticeUntil;

    protected override colorHDR VisualTint => new(0.95f, 0.3f, 0.28f, 1f);

    public int FlashCount => _flash.Count;

    public string? NoticeText => _noticeSlot is { IsDestroyed: false, ActiveSelf.Value: true } ? _notice?.Text.Value : null;

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Camera Leave-Out Tool";
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.011f, FlashTint);
    }

    public override bool OnPrimaryPress()
    {
        // A panel's presses are the panel's.
        if (ActiveTool?.Laser?.CurrentPointerTarget != null)
            return false;
        var camera = PhotoCamera.MineIn(World);
        if (camera == null)
        {
            Notice("you have no camera in this world");
            return true;
        }
        if (!TryGetAim(out var aim) || Pick(aim.HitSlot, camera) is not { } target)
            return false;
        if (!camera.LeftOut.Contains(target) && !Guarded(() => camera.LeftOut.Add(target)))
            return true;
        Flash(target);
        return true;
    }

    // With nothing to put back the press falls through, so the menu still opens on it in VR.
    public override bool OnSecondaryPress()
    {
        if (ActiveTool?.Laser?.CurrentPointerTarget != null)
            return false;
        var camera = PhotoCamera.MineIn(World);
        if (camera == null || !TryGetAim(out var aim))
            return false;
        for (var slot = aim.HitSlot; slot != null; slot = slot.Parent)
        {
            if (!camera.LeftOut.Contains(slot))
                continue;
            var listed = slot;
            Guarded(() => camera.LeftOut.Remove(listed));
            return true;
        }
        return false;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        double now = World?.Time.TotalTime ?? 0.0;
        if (_flash.Count > 0)
        {
            double left = 1.0 - (now - _flashStart) / FlashSeconds;
            if (left <= 0.0)
                EndFlash();
            else
                UpdateFlash((float)left);
        }
        if (_noticeSlot is { IsDestroyed: false } notice && notice.ActiveSelf.Value && now > _noticeUntil)
            notice.ActiveSelf.Value = false;
    }

    public override void OnDestroy()
    {
        EndFlash();
        if (_marks is { IsDestroyed: false })
            _marks.Destroy();
        _marks = null;
        base.OnDestroy();
    }

    // What a press means. The list is shared, so it walks up past anything only this machine has, and it
    // never takes the world root or the camera itself.
    private static Slot? Pick(Slot? hit, PhotoCamera camera)
    {
        if (hit == null || hit.IsDestroyed || ReferenceEquals(hit, camera.Slot) || hit.IsDescendantOf(camera.Slot))
            return null;
        var person = hit.ActiveUserRoot?.Slot;
        for (var current = hit; current != null && !current.IsRootSlot && !ReferenceEquals(current, person); current = current.Parent)
        {
            var grab = current.GetComponent<Grabbable>();
            bool marked = current.GetComponent<ObjectRoot>() != null || grab != null;
            // On a person only what they hold is a thing of its own; the rest of them is them.
            if (marked && (person == null || grab is { IsGrabbed: true }))
                return Shared(current);
        }
        return Shared(person ?? hit);
    }

    private static Slot? Shared(Slot? slot)
    {
        while (slot != null && slot.IsLocalElement)
            slot = slot.Parent;
        return slot == null || slot.IsRootSlot ? null : slot;
    }

    // EYES-ONLY MARKS
    //
    // One local slot for everything this tool draws: never sent, never saved, never filmed.

    private Slot? Marks()
    {
        if (_marks is { IsDestroyed: false })
            return _marks;
        var root = World?.RootSlot;
        if (root == null)
            return null;
        _marks = root.AddLocalSlot("Leave-Out Marks");
        _marks.AttachComponent<RenderLayerOverride>().Layer.Value = RenderLayerOverride.EyesOnlyLayer;
        return _marks;
    }

    // Every mesh under it drawn over in the flash colour, or a box round it when there is no mesh to copy (a
    // skinned body, a bare collider), following it while it moves and fading out.
    private void Flash(Slot target)
    {
        EndFlash();
        var marks = Marks();
        if (marks == null)
            return;
        if (_flashMaterial is not { IsDestroyed: false } material)
        {
            material = marks.AttachComponent<OverlayUnlitMaterial>();
            material.UseVertexColor.Value = false;
            _flashMaterial = material;
        }
        CopyMeshes(target, marks, material);
        if (_flash.Count == 0 && SlotBoundsHelper.TryComputeLocalBounds(target, out var bounds))
        {
            var copy = marks.AddLocalSlot("Flash");
            var box = copy.AddLocalSlot("Box");
            box.LocalPosition.Value = bounds.Center;
            var mesh = box.AttachComponent<BoxMesh>();
            mesh.Size.Value = bounds.Size;
            Draw(box, mesh, 1, material);
            _flash.Add((target, copy));
        }
        _flashStart = World?.Time.TotalTime ?? 0.0;
        UpdateFlash(1f);
    }

    private void CopyMeshes(Slot slot, Slot marks, OverlayUnlitMaterial material)
    {
        if (slot.IsDestroyed || !slot.IsActive || _flash.Count >= FlashPartLimit)
            return;
        foreach (var component in slot.Components)
        {
            if (_flash.Count >= FlashPartLimit)
                return;
            if (component is not MeshRenderer { IsDestroyed: false } renderer || !renderer.Enabled.Value || renderer.Mesh.Target == null)
                continue;
            var copy = marks.AddLocalSlot("Flash");
            Draw(copy, renderer.Mesh.Target, System.Math.Max(renderer.Materials.Count, 1), material);
            _flash.Add((slot, copy));
        }
        foreach (var child in slot.Children)
            CopyMeshes(child, marks, material);
        foreach (var child in slot.LocalChildren)
            CopyMeshes(child, marks, material);
    }

    private static void Draw(Slot slot, Component mesh, int surfaces, OverlayUnlitMaterial material)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        for (int i = 0; i < surfaces; i++)
            renderer.Materials.Add(material);
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    private void UpdateFlash(float strength)
    {
        if (_flashMaterial is { IsDestroyed: false } material)
        {
            material.FrontTintColor.Value = new colorHDR(FlashTint.r * strength, FlashTint.g * strength, FlashTint.b * strength, strength);
            float behind = strength * 0.4f;
            material.BehindTintColor.Value = new colorHDR(FlashTint.r * behind, FlashTint.g * behind, FlashTint.b * behind, behind);
        }
        // The marks slot sits at the world origin, so a copy's local pose is its source's world pose.
        foreach (var (source, copy) in _flash)
        {
            if (source.IsDestroyed || copy.IsDestroyed)
                continue;
            copy.LocalPosition.Value = source.GlobalPosition;
            copy.LocalRotation.Value = source.GlobalRotation;
            copy.LocalScale.Value = source.GlobalScale;
        }
    }

    private void EndFlash()
    {
        foreach (var (_, copy) in _flash)
        {
            if (!copy.IsDestroyed)
                copy.Destroy();
        }
        _flash.Clear();
    }

    // A short note by the pointer, for this user only.
    private void Notice(string text)
    {
        LumoraLogger.Log($"Camera Leave-Out Tool: {text}");
        var marks = Marks();
        if (marks == null)
            return;
        if (_noticeSlot is not { IsDestroyed: false } || _notice == null)
        {
            _noticeSlot = marks.AddLocalSlot("Notice");
            _notice = _noticeSlot.AttachComponent<TextRenderer>();
            var font = _noticeSlot.AttachComponent<FontProvider>();
            font.URL.Value = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
            _notice.Font.Target = font;
            _notice.Size.Value = 0.018f;
            _notice.Color.Value = DashTheme.Warning;
            _notice.HorizontalAlign.Value = TextHorizontalAlignment.Center;
            _notice.OutlineThickness.Value = 2f;
            _notice.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 1f);
        }
        var laser = ActiveTool?.Laser;
        var at = laser == null
            ? Tip
            : laser.RayOrigin + laser.RayDirection * MathF.Min(laser.CurrentHitSlot != null ? laser.CurrentHitDistance : NoticeReach, NoticeReach);
        _noticeSlot.LocalPosition.Value = at + float3.Up * 0.04f;
        // It reads along its own +Z, so it turns about up toward the viewer.
        if (World?.LocalUser?.Root?.HeadPosition is { } eye)
        {
            var toViewer = eye - at;
            toViewer.y = 0f;
            if (toViewer.LengthSquared > 1e-8f)
                _noticeSlot.LocalRotation.Value = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));
        }
        _noticeSlot.ActiveSelf.Value = true;
        _notice.Text.Value = text;
        _noticeUntil = (World?.Time.TotalTime ?? 0.0) + NoticeSeconds;
    }
}
