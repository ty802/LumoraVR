// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Draws the hand menu's overlay UI shaders once at startup, so the first menu open does not compile them.
// The ring is the only thing besides the keyboard drawing UI over the world, and Godot compiles a shader
// the first time a frame draws it, on the main thread. This puts a throwaway overlay canvas in userspace
// in front of the view, a few ring icons and a line of the ring's font at an alpha nobody can see, for a
// few frames once its pictures and font have loaded, then removes itself. It has to be in view: a culled
// mesh never draws and warms nothing. Userspace is local, so none of it ever leaves this machine. -xlinka
[ComponentCategory("Hidden")]
public sealed class OverlayShaderWarmUp : Component
{
    private const float Unseen = 0.01f;
    private const float Distance = 1f;
    private const float ParkDepth = 1000f;
    private const float CanvasScale = 0.001f;
    private const ulong DrawFrames = 10;
    private const double GiveUpSeconds = 60.0;

    private static readonly string[] Icons = { "undo", "laser", "inspect", "trash", "close", "plus", "forward" };

    private Slot? _canvasSlot;
    private FontProvider? _font;
    private readonly ImageProvider?[] _icons = new ImageProvider?[Icons.Length];
    private double _startedAt = double.NaN;
    private ulong _readyFrame;
    private bool _ready;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (World is not { } world || Slot == null || Slot.IsDestroyed)
            return;

        double now = world.Time.TotalTime;
        if (double.IsNaN(_startedAt))
            _startedAt = now;
        if (now - _startedAt > GiveUpSeconds)
        {
            Finish();
            return;
        }

        // Userspace sits over the focused world in that world's own coordinates; nothing focused, nothing drawn.
        var root = Engine.Current?.WorldManager?.FocusedWorld?.LocalUser?.Root;
        if (root?.HeadSlot == null)
            return;

        using var bypass = world.DataModelPermissions?.EnterSystemBypass();
        if (_canvasSlot == null)
            Build();
        if (_canvasSlot == null || _canvasSlot.IsDestroyed)
        {
            Finish();
            return;
        }

        if (!_ready)
        {
            // A canvas is a laser target, so it waits for its font and pictures far out of every beam's way.
            _canvasSlot.GlobalPosition = root.HeadPosition + float3.Down * ParkDepth;
            if (!Loaded())
                return;
            _ready = true;
            _readyFrame = world.Time.UpdateIndex;
        }

        var headRotation = root.HeadRotation;
        _canvasSlot.GlobalPosition = root.HeadPosition + headRotation * (float3.Backward * Distance);
        _canvasSlot.GlobalRotation = headRotation;
        if (world.Time.UpdateIndex - _readyFrame >= DrawFrames)
            Finish();
    }

    private bool Loaded()
    {
        if (_font is not { IsDestroyed: false, IsAssetAvailable: true })
            return false;
        foreach (var icon in _icons)
        {
            if (icon is not { IsDestroyed: false, IsAssetAvailable: true })
                return false;
        }
        return true;
    }

    private void Build()
    {
        _canvasSlot = Slot.AddSlot("Canvas");
        _canvasSlot.Persistent.Value = false;
        _canvasSlot.LocalScale.Value = float3.One * CanvasScale;

        _font = _canvasSlot.AttachComponent<FontProvider>();
        _font.URL.Value = DashTheme.FontSemibold;

        var rootRect = _canvasSlot.AttachComponent<RectTransform>();
        rootRect.OffsetMin.Value = new float2(-160f, -60f);
        rootRect.OffsetMax.Value = new float2(160f, 60f);
        var canvas = _canvasSlot.AttachComponent<Canvas>();
        canvas.SortingOrder.Value = 10000;
        canvas.Overlay.Value = true;
        canvas.Culling.Value = Lumora.Core.Assets.Culling.None;
        canvas.TextOutlineThickness.Value = 0f;
        canvas.TextFaceDilate.Value = 0f;

        var hidden = new color(1f, 1f, 1f, Unseen);

        var ring = _canvasSlot.AddSlot("Ring");
        Place(ring.AttachComponent<RectTransform>(), new float2(-110f, 0f), new float2(80f, 80f));
        var arc = ring.AttachComponent<ArcSegment>();
        arc.AngleStart.Value = 0f;
        arc.ArcLength.Value = 360f;
        arc.InnerRadius.Value = 20f;
        arc.OuterRadius.Value = 38f;
        arc.OutlineThickness.Value = 1f;
        arc.Tint.Value = hidden;
        arc.OutlineColor.Value = hidden;

        for (int i = 0; i < Icons.Length; i++)
        {
            var provider = _canvasSlot.AttachComponent<ImageProvider>();
            provider.URL.Value = new Uri("res://Assets/Textures/Icons/" + Icons[i] + ".svg");
            _icons[i] = provider;

            var slot = _canvasSlot.AddSlot("Icon");
            Place(slot.AttachComponent<RectTransform>(), new float2(-48f + i * 24f, 30f), new float2(20f, 20f));
            var image = slot.AttachComponent<Image>();
            image.Texture.Target = provider;
            image.PreserveAspect.Value = true;
            image.InteractionTarget.Value = false;
            image.Tint.Value = hidden;
        }

        var label = _canvasSlot.AddSlot("Label");
        Place(label.AttachComponent<RectTransform>(), new float2(30f, -20f), new float2(220f, 30f));
        var text = label.AttachComponent<Text>();
        text.Font.Target = _font;
        text.Size.Value = 15f;
        text.Color.Value = hidden;
        text.InteractionTarget.Value = false;
        text.Content.Value = "Undo Redo Inspector Laser 1/2";
    }

    private static void Place(RectTransform rect, float2 at, float2 size)
    {
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = at - size * 0.5f;
        rect.OffsetMax.Value = at + size * 0.5f;
    }

    private void Finish()
    {
        if (Slot == null || Slot.IsDestroyed)
            return;
        Slot.Destroy();
    }
}
