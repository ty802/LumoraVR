// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Click two points, read the distance between them.
//
// The readout is a LOCAL slot and everything under it is local with it - the measuring is yours, not
// the room's, and a tape measure that left synced geometry lying around the world would be a
// vandalism tool with a number on it. Local also means it survives the Social/Event floor: a local
// element is minted in your own byte, so it is yours to write even where the authored world is
// frozen. That is the whole reason this tool works in a world where none of the others do. -xlinka
//
// The first point is transient aim state on this hand and is deliberately NOT a sync member. The
// second pair replaces the first: one readout at a time, so the world does not slowly fill with
// measurements you forgot you made.
[ComponentCategory("Interaction/Tools")]
public sealed class MeterTool : EquippableTool
{
    private const string ReadoutName = "Meter Readout";
    private const string FallbackFontPath = "res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf";
    private const float PointRadius = 0.012f;
    private const float LineRadius = 0.004f;

    private static readonly colorHDR ReadoutTint = new(0.35f, 0.95f, 1f, 1f);

    private float3 _firstPoint;
    private bool _hasFirst;
    private Slot? _readout;
    private Slot? _bead;

    protected override colorHDR VisualTint => new(0.3f, 0.85f, 0.95f, 1f);

    public bool HasFirstPoint => _hasFirst;

    public Slot? Readout => _readout;

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Meter Tool";
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        _bead = EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.011f, VisualTint);
    }

    // No EditingBlocked gate: nothing this tool writes is world content.
    public override bool OnPrimaryPress()
    {
        if (!TryGetAim(out var aim))
            return false;

        if (!_hasFirst)
        {
            _firstPoint = aim.Point;
            _hasFirst = true;
            BuildReadout(aim.Point, aim.Point, single: true);
            SetBeadTint(_bead, new colorHDR(1f, 0.85f, 0.25f, 1f));
            return true;
        }

        _hasFirst = false;
        BuildReadout(_firstPoint, aim.Point, single: false);
        SetBeadTint(_bead, VisualTint);
        return true;
    }

    public override bool OnSecondaryPress()
    {
        if (!_hasFirst && _readout == null)
            return false;
        ClearReadout();
        return true;
    }

    public override void OnDequipped()
    {
        base.OnDequipped();
        ClearReadout();
    }

    public override void OnDestroy()
    {
        ClearReadout();
        base.OnDestroy();
    }

    public void ClearReadout()
    {
        _hasFirst = false;
        SetBeadTint(_bead, VisualTint);
        if (_readout != null && !_readout.IsDestroyed)
            _readout.Destroy();
        _readout = null;
    }

    private void BuildReadout(float3 a, float3 b, bool single)
    {
        var root = World?.RootSlot;
        if (root == null)
            return;

        if (_readout != null && !_readout.IsDestroyed)
            _readout.Destroy();
        _readout = null;

        // AddLocalSlot, not AddSlot: this must never be replicated, saved or duplicated. Everything
        // built under it inherits that - a child of a local slot is allocated local, and so is a
        // component attached to one.
        var readout = root.AddLocalSlot(ReadoutName);
        readout.Persistent.Value = false;
        _readout = readout;

        SpawnPoint(readout, "Point A", a);
        if (single)
            return;

        SpawnPoint(readout, "Point B", b);

        var lineSlot = readout.AddSlot("Line");
        lineSlot.GlobalPosition = float3.Zero;
        var segment = lineSlot.AttachComponent<SegmentMesh>();
        segment.Radius.Value = LineRadius;
        segment.Sides.Value = 6;
        segment.PointA.Value = lineSlot.GlobalPointToLocal(a);
        segment.PointB.Value = lineSlot.GlobalPointToLocal(b);
        var lineMaterial = lineSlot.AttachComponent<UnlitMaterial>();
        lineMaterial.TintColor.Value = ReadoutTint;
        lineMaterial.UseVertexColor.Value = false;
        var lineRenderer = lineSlot.AttachComponent<MeshRenderer>();
        lineRenderer.Mesh.Target = segment;
        lineRenderer.Material.Target = lineMaterial;
        lineRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        BuildLabel(readout, (a + b) * 0.5f, float3.Distance(a, b));
    }

    private void SpawnPoint(Slot readout, string name, float3 position)
    {
        var slot = readout.AddSlot(name);
        slot.GlobalPosition = position;

        var mesh = slot.AttachComponent<SphereMesh>();
        mesh.Radius.Value = PointRadius;
        mesh.Segments.Value = 12;
        mesh.Rings.Value = 8;

        var material = slot.AttachComponent<UnlitMaterial>();
        material.TintColor.Value = ReadoutTint;
        material.UseVertexColor.Value = false;

        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    private void BuildLabel(Slot readout, float3 midpoint, float distance)
    {
        var slot = readout.AddSlot("Label");
        slot.GlobalPosition = midpoint + float3.Up * 0.06f;
        FaceViewer(slot);

        // Text with no font renders NOTHING, so the label has to bring its own provider. The dashboard
        // registers a URL at startup; a headless run or a template that spawns this itself has none,
        // hence the fallback.
        var font = slot.AttachComponent<FontProvider>();
        font.URL.Value = ImportDialog.ResolveFontUrl(World) ?? new Uri(FallbackFontPath);

        var text = slot.AttachComponent<TextRenderer>();
        text.Text.Value = $"{distance:0.###} m";
        text.Size.Value = 0.06f;
        text.Color.Value = new color(0.92f, 0.99f, 1f, 1f);
        text.Font.Target = font;
        text.HorizontalAlign.Value = Helio.UI.TextHorizontalAlignment.Center;
        text.VerticalAlign.Value = Helio.UI.TextVerticalAlignment.Middle;
        text.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 0.9f);
        text.OutlineThickness.Value = 1.05f;
    }

    // A TextRenderer's readable face points down its own +Z, so the yaw that turns one toward the
    // viewer is the angle of the flattened viewer-ward vector: atan2(x, z), because yaw here is
    // measured off +Z. Not LookRotation - it hands back the INVERSE and the label goes edge-on the
    // moment the reader is off-axis.
    private void FaceViewer(Slot slot)
    {
        var eye = World?.LocalUser?.Root?.HeadPosition;
        if (eye == null)
            return;
        var toViewer = eye.Value - slot.GlobalPosition;
        toViewer.y = 0f;
        if (toViewer.LengthSquared <= 1e-6f)
            return;
        slot.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        if (!_hasFirst && _readout == null)
            return;

        page.AddItem(new ContextMenuItem
        {
            Label = "Clear Measurement",
            FillColor = ClearFill,
            OnPressed = _ => { if (!IsDestroyed) ClearReadout(); },
        });
    }
}
