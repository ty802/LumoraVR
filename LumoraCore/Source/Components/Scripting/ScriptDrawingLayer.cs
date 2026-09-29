// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// What scripts draw on the machine they run on: shapes, labels and flashed highlights that last a set time
// and are never saved or sent. One per world, on a local slot under its root, made the first time a script
// draws there. Times run on the world's frame clock, so a paused world's drawings wait with it, and
// anything drawn shows for at least one frame whatever its time. Shapes draw where they are, hidden behind
// what stands in front; a highlight draws over everything, dimmer where something stands in front of it,
// and fades out over its time. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptDrawingLayer : Component
{
    public const float OutlineRadius = 0.003f;
    private const int SpareLabels = 64;

    private static readonly ConditionalWeakTable<World, ScriptDrawingLayer> _layers = new();

    private readonly List<Drawn> _shapes = new();
    private readonly List<Label> _labels = new();
    private readonly List<Flash> _flashes = new();
    private readonly Stack<Label> _spare = new();
    private readonly List<ScriptDrawnShape> _building = new();
    private ScriptDrawingMesh? _drawings;
    private ScriptDrawingMesh? _overlay;
    private FontProvider? _font;
    private bool _dirty;
    private bool _overlayShown;

    private struct Drawn
    {
        public ScriptDrawnShape Shape;
        public double Until;
        public bool Shown;
    }

    private sealed class Label
    {
        public Slot Slot = null!;
        public TextRenderer Text = null!;
        public double Until;
        public bool Shown;
    }

    // One flashed renderer or collider: a copy of the renderer's mesh and outline segments, in the flashed
    // slot's own space while it is followed, else in world space as it stood.
    private sealed class FlashPart
    {
        public Slot Source = null!;
        public Slot? Copy;
        public OverlayUnlitMaterial? Material;
        public readonly List<(float3 A, float3 B)> Lines = new();
    }

    private sealed class Flash
    {
        public bool Follow;
        public double Start;
        public double Until;
        public color Color;
        public readonly List<FlashPart> Parts = new();
    }

    public int ShapeCount => _shapes.Count;

    public int LabelCount => _labels.Count;

    public int FlashCount => _flashes.Count;

    // What the shape mesh holds, for whoever checks what was drawn.
    public int DrawnCount => _drawings?.ShapeCount ?? 0;

    public void GetShapes(List<ScriptDrawnShape> into)
    {
        foreach (var drawn in _shapes)
            into.Add(drawn.Shape);
    }

    public static ScriptDrawingLayer? Find(World world)
        => _layers.TryGetValue(world, out var layer) && !layer.IsDestroyed ? layer : null;

    public static ScriptDrawingLayer For(World world)
    {
        if (Find(world) is { } found)
            return found;
        var slot = world.RootSlot.AddLocalSlot("Script Drawings");
        var layer = slot.AttachComponent<ScriptDrawingLayer>();
        _layers.AddOrUpdate(world, layer);
        return layer;
    }

    private double Now => World?.Time.TotalTime ?? 0.0;

    public void Add(in ScriptDrawnShape shape, float seconds)
    {
        _shapes.Add(new Drawn { Shape = shape, Until = Now + System.Math.Max(seconds, 0f) });
        _dirty = true;
    }

    public void AddLabel(float3 position, string text, float size, color color, float seconds)
    {
        var label = _spare.Count > 0 ? _spare.Pop() : MakeLabel();
        label.Slot.ActiveSelf.Value = true;
        label.Slot.LocalPosition.Value = position;
        label.Text.Text.Value = text;
        label.Text.Size.Value = size;
        label.Text.Color.Value = color;
        label.Until = Now + System.Math.Max(seconds, 0f);
        label.Shown = false;
        _labels.Add(label);
        Face(label.Slot);
    }

    // Flashes the slot and what is under it: every mesh as a copy drawn in the colour, every collider and
    // every skinned mesh as an outline of its shape. Returns how many things were flashed.
    public int AddFlash(Slot source, color color, float seconds, bool meshes, bool colliders, bool hidden, bool follow)
    {
        var flash = new Flash
        {
            Follow = follow,
            Start = Now,
            Until = Now + System.Math.Max(seconds, 0f),
            Color = color,
        };
        Collect(flash, source, meshes, colliders, hidden);
        if (flash.Parts.Count == 0)
            return 0;
        _flashes.Add(flash);
        UpdateFlash(flash, 1f);
        return flash.Parts.Count;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (IsDestroyed || World == null)
            return;
        double now = Now;

        int kept = 0;
        for (int i = 0; i < _shapes.Count; i++)
        {
            var drawn = _shapes[i];
            if (drawn.Shown && drawn.Until < now)
                continue;
            drawn.Shown = true;
            _shapes[kept++] = drawn;
        }
        if (kept != _shapes.Count)
        {
            _shapes.RemoveRange(kept, _shapes.Count - kept);
            _dirty = true;
        }
        if (_dirty)
        {
            _dirty = false;
            _building.Clear();
            foreach (var drawn in _shapes)
                _building.Add(drawn.Shape);
            (_drawings ??= MakeMesh("Shapes", overlay: false)).SetShapes(_building);
        }

        for (int i = _labels.Count - 1; i >= 0; i--)
        {
            var label = _labels[i];
            if (label.Shown && label.Until < now)
            {
                _labels.RemoveAt(i);
                Park(label);
                continue;
            }
            label.Shown = true;
            Face(label.Slot);
        }

        _building.Clear();
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var flash = _flashes[i];
            double span = flash.Until - flash.Start;
            float fade = span > 0.0 ? (float)System.Math.Clamp(1.0 - (now - flash.Start) / span, 0.0, 1.0) : 0f;
            if (now > flash.Until && now > flash.Start)
            {
                foreach (var part in flash.Parts)
                    part.Copy?.Destroy();
                _flashes.RemoveAt(i);
                continue;
            }
            UpdateFlash(flash, span > 0.0 ? fade : 1f);
        }
        if (_building.Count > 0 || _overlayShown)
        {
            (_overlay ??= MakeMesh("Highlights", overlay: true)).SetShapes(_building);
            _overlayShown = _building.Count > 0;
        }
    }

    public override void OnDestroy()
    {
        if (World != null && _layers.TryGetValue(World, out var layer) && ReferenceEquals(layer, this))
            _layers.Remove(World);
        base.OnDestroy();
    }

    private void UpdateFlash(Flash flash, float fade)
    {
        var tint = new color(flash.Color.r * fade, flash.Color.g * fade, flash.Color.b * fade, flash.Color.a * fade);
        foreach (var part in flash.Parts)
        {
            bool alive = !part.Source.IsDestroyed;
            if (part.Copy != null && !part.Copy.IsDestroyed)
            {
                if (flash.Follow && alive)
                    Place(part.Copy, part.Source);
                if (part.Material != null)
                {
                    part.Material.FrontTintColor.Value = tint;
                    part.Material.BehindTintColor.Value = new colorHDR(tint.r * 0.4f, tint.g * 0.4f, tint.b * 0.4f, tint.a * 0.4f);
                }
            }
            foreach (var (a, b) in part.Lines)
            {
                if (flash.Follow && alive)
                    _building.Add(ScriptDrawnShape.Segment(part.Source.LocalPointToGlobal(a), part.Source.LocalPointToGlobal(b), tint, OutlineRadius));
                else if (!flash.Follow)
                    _building.Add(ScriptDrawnShape.Segment(a, b, tint, OutlineRadius));
            }
        }
    }

    private void Collect(Flash flash, Slot slot, bool meshes, bool colliders, bool hidden)
    {
        if (slot.IsDestroyed || (!hidden && !slot.IsActive))
            return;
        foreach (var component in slot.Components)
        {
            if (component.IsDestroyed || (!hidden && !component.Enabled.Value))
                continue;
            switch (component)
            {
                case MeshRenderer renderer when meshes && renderer.Mesh.Target != null:
                    flash.Parts.Add(CopyOf(flash, slot, renderer));
                    break;
                case SkinnedMeshRenderer skinned when meshes && skinned.MeshAsset.Asset is { } asset:
                    flash.Parts.Add(Outline(flash, slot, asset.Bounds, sphere: false));
                    break;
                case SphereCollider sphere when colliders:
                    flash.Parts.Add(Outline(flash, slot, sphere.GetLocalBounds(), sphere: true));
                    break;
                case Collider collider when colliders:
                    flash.Parts.Add(Outline(flash, slot, collider.GetLocalBounds(), sphere: false));
                    break;
            }
        }
        foreach (var child in slot.Children)
            Collect(flash, child, meshes, colliders, hidden);
    }

    private FlashPart CopyOf(Flash flash, Slot source, MeshRenderer original)
    {
        var copy = Slot.AddLocalSlot("Highlight");
        Place(copy, source);
        var material = copy.AttachComponent<OverlayUnlitMaterial>();
        material.UseVertexColor.Value = false;
        var renderer = copy.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = original.Mesh.Target;
        int surfaces = System.Math.Max(original.Materials.Count, 1);
        for (int i = 0; i < surfaces; i++)
            renderer.Materials.Add(material);
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        return new FlashPart { Source = source, Copy = copy, Material = material };
    }

    private static FlashPart Outline(Flash flash, Slot source, BoundingBox bounds, bool sphere)
    {
        var part = new FlashPart { Source = source };
        var lines = new List<(float3, float3)>();
        if (sphere)
            ScriptDrawingShapes.Rings(bounds.Center, bounds.Extents.x, floatQ.Identity, lines);
        else
            ScriptDrawingShapes.BoxEdges(bounds.Center, bounds.Size, floatQ.Identity, lines);
        foreach (var (a, b) in lines)
            part.Lines.Add(flash.Follow ? (a, b) : (source.LocalPointToGlobal(a), source.LocalPointToGlobal(b)));
        return part;
    }

    private static void Place(Slot copy, Slot source)
    {
        copy.LocalPosition.Value = source.GlobalPosition;
        copy.LocalRotation.Value = source.GlobalRotation;
        copy.LocalScale.Value = source.GlobalScale;
    }

    private ScriptDrawingMesh MakeMesh(string name, bool overlay)
    {
        var slot = Slot.AddLocalSlot(name);
        var mesh = slot.AttachComponent<ScriptDrawingMesh>();
        MaterialProvider material;
        if (overlay)
        {
            var highlight = slot.AttachComponent<OverlayUnlitMaterial>();
            highlight.UseVertexColor.Value = true;
            highlight.FrontTintColor.Value = colorHDR.White;
            highlight.BehindTintColor.Value = new colorHDR(0.4f, 0.4f, 0.4f, 0.4f);
            material = highlight;
        }
        else
        {
            var unlit = slot.AttachComponent<UnlitMaterial>();
            unlit.UseVertexColor.Value = true;
            unlit.TintColor.Value = colorHDR.White;
            unlit.BlendMode.Value = BlendMode.Alpha;
            unlit.Culling.Value = Culling.None;
            material = unlit;
        }
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        return mesh;
    }

    private Label MakeLabel()
    {
        if (_font == null || _font.IsDestroyed)
        {
            _font = Slot.AttachComponent<FontProvider>();
            var url = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
            _font.URL.Value = url;
        }
        var slot = Slot.AddLocalSlot("Label");
        var text = slot.AttachComponent<TextRenderer>();
        text.Font.Target = _font;
        text.HorizontalAlign.Value = Helio.UI.TextHorizontalAlignment.Center;
        text.VerticalAlign.Value = Helio.UI.TextVerticalAlignment.Middle;
        text.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 0.9f);
        text.OutlineThickness.Value = 1f;
        return new Label { Slot = slot, Text = text };
    }

    private void Park(Label label)
    {
        if (label.Slot.IsDestroyed)
            return;
        if (_spare.Count >= SpareLabels)
        {
            label.Slot.Destroy();
            return;
        }
        label.Slot.ActiveSelf.Value = false;
        _spare.Push(label);
    }

    // A label reads along its own +Z, so it turns about up by the angle of the flattened line to the viewer.
    private void Face(Slot slot)
    {
        var eye = World?.LocalUser?.Root?.HeadPosition;
        if (eye == null)
            return;
        var toViewer = eye.Value - slot.GlobalPosition;
        toViewer.y = 0f;
        if (toViewer.LengthSquared <= 1e-8f)
            return;
        slot.LocalRotation.Value = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z));
    }
}
