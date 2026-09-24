// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using System;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>A framed mirror with local controls over the glass. Attach Mirror for a bare surface.</summary>
[ComponentCategory("Rendering")]
public sealed class MirrorPanel : Component, IInspectorActionHandler
{
    public readonly Sync<float2> Size;
    public readonly SyncRef<Mirror> Portal;
    private const float FrameMargin = 0.02f;
    private const float CanvasWidth = 560f;
    private const float CanvasHeight = 396f;
    private Slot? _frameSlot, _controlsSlot, _contentSlot;
    private FontProvider? _font;
    private bool _settingsOpen, _refreshControls;

    public MirrorPanel()
    {
        Size = new Sync<float2>(this, new float2(2.6f, 2.2f));
        Portal = new SyncRef<Mirror>(this);
    }

    public static MirrorPanel Spawn(Slot parent, float3 position, float3 facing, float2 size)
    {
        var root = parent.AddSlot("Mirror");
        root.GlobalPosition = position;
        facing.y = 0f;
        if (facing.LengthSquared < 1e-6f) facing = float3.Backward;
        facing = facing.Normalized;
        root.GlobalRotation = floatQ.AxisAngleRad(float3.Up, MathF.Atan2(-facing.x, -facing.z));
        if (float3.Dot(root.Backward, facing) < 0f)
            root.GlobalRotation = root.GlobalRotation * floatQ.AxisAngleRad(float3.Up, MathF.PI);
        var panel = root.AttachComponent<MirrorPanel>();
        panel.Size.Value = size;
        return panel;
    }

    public override void OnAttach()
    {
        base.OnAttach();
        Portal.Target = Slot.AddSlot("Glass").AttachComponent<Mirror>();
        Slot.GetOrAttachComponent<BoxCollider>();
        Slot.GetOrAttachComponent<Grabbable>();
        BuildFrame();
        Layout();
    }

    public override void OnStart()
    {
        base.OnStart();
        BuildFrame();
        // Replace the previous under-glass bar when loading an existing panel.
        Slot.FindChild("Controls")?.Destroy();
        Slot.FindChild("ControlsFrame")?.Destroy();
        _controlsSlot = Slot.AddLocalSlot("Mirror controls");
        _controlsSlot.AttachComponent<RenderLayerOverride>().Layer.Value = RenderLayerOverride.MirrorLayer;
        var rect = _controlsSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(-CanvasWidth / 2f, -CanvasHeight / 2f);
        rect.OffsetMax.Value = new float2(CanvasWidth / 2f, CanvasHeight / 2f);
        _controlsSlot.AttachComponent<Canvas>();
        _font = _controlsSlot.AddSlot("Font").AttachComponent<FontProvider>();
        _font.URL.Value = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
        _font.FallbackURLs.Add(DashTheme.FontRegular);
        _contentSlot = _controlsSlot.AddSlot("Content");
        FillRect(_contentSlot);
        _contentSlot.AttachComponent<GraphicChunkRoot>();
        Layout();
        RebuildContent();
    }

    public override void OnChanges() { base.OnChanges(); Layout(); }
    public override void OnDestroy()
    {
        if (_controlsSlot != null && !_controlsSlot.IsDestroyed) _controlsSlot.Destroy();
        _controlsSlot = null;
        _contentSlot = null;
        base.OnDestroy();
    }

    public override void OnUpdate(float deltaTime)
    {
        base.OnUpdate(deltaTime);
        if (!_refreshControls) return;
        _refreshControls = false;
        RebuildContent();
    }

    private void BuildFrame()
    {
        _frameSlot = Slot.FindChildOrAdd("Frame");
        var mesh = _frameSlot.GetOrAttachComponent<RoundedQuadMesh>();
        mesh.CornerRadius.Value = FrameMargin;
        mesh.CornerSegments.Value = 4;
        mesh.DualSided.Value = false;
        _frameSlot.LocalPosition.Value = new float3(0f, 0f, 0.004f);
        _frameSlot.LocalRotation.Value = floatQ.AxisAngleRad(float3.Up, MathF.PI);
        _frameSlot.GetOrAttachComponent<RenderLayerOverride>().Layer.Value = RenderLayerOverride.MirrorLayer;
        var renderer = _frameSlot.GetOrAttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = DashGlass.Attach(_frameSlot);
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    private void Layout()
    {
        if (Slot == null || Slot.IsDestroyed) return;
        var size = new float2(MathF.Max(0.2f, Size.Value.x), MathF.Max(0.2f, Size.Value.y));
        if (Portal.Target is { } mirror && mirror.Size.Value != size) mirror.Size.Value = size;
        if (_frameSlot?.GetComponent<RoundedQuadMesh>() is { } frame)
            frame.Size.Value = new float2(size.x + FrameMargin * 2f, size.y + FrameMargin * 2f);
        if (Slot.GetComponent<BoxCollider>() is { } collider)
        {
            collider.Size.Value = new float3(size.x + FrameMargin * 2f, size.y + FrameMargin * 2f, 0.02f);
            collider.Offset.Value = float3.Zero;
        }
        if (_controlsSlot == null || _controlsSlot.IsDestroyed) return;
        // Bottom-centred on the glass, with the settings opening upwards.
        float scale = MathF.Min(0.0022f, MathF.Min(size.x * 0.9f / CanvasWidth, size.y * 0.9f / CanvasHeight));
        float inset = MathF.Min(0.055f, MathF.Min(size.x, size.y) * 0.04f);
        _controlsSlot.LocalScale.Value = new float3(scale, scale, scale);
        _controlsSlot.LocalRotation.Value = floatQ.AxisAngleRad(float3.Up, MathF.PI);
        _controlsSlot.LocalPosition.Value = new float3(0f,
            -size.y * 0.5f + inset + CanvasHeight * scale * 0.5f, -0.02f);
    }

    private void RebuildContent()
    {
        if (_contentSlot == null || _contentSlot.IsDestroyed || Portal.Target is not { } mirror) return;
        _contentSlot.DestroyChildren();
        var toolbar = _contentSlot.AddSlot("Toolbar");
        BottomCentreRect(toolbar, 300f, 72f, 0f);
        var ui = Builder(toolbar);
        ui.HorizontalLayout(8f);
        FillRect(ui.Current);
        ui.FlexibleWidth(1f);
        ui.Button("Mirror", OnPowerPressed, mirror.LocalRenderingEnabled ? DashTheme.AccentSoft : DashTheme.Surface);
        ui.FlexibleWidth(0f).MinWidth(72f).PreferredWidth(72f);
        var settings = ui.Button(string.Empty, OnSettingsPressed, _settingsOpen ? DashTheme.AccentSoft : DashTheme.Surface);
        settings.Slot.SlotName.Value = "Settings";
        var glyph = settings.Slot.AddSlot("Cog");
        var iconRect = glyph.AttachComponent<RectTransform>();
        iconRect.AnchorMin.Value = iconRect.AnchorMax.Value = new float2(0.5f, 0.5f);
        iconRect.OffsetMin.Value = new float2(-16f, -16f);
        iconRect.OffsetMax.Value = new float2(16f, 16f);
        var picture = glyph.AttachComponent<ImageProvider>();
        picture.GenerateMipmaps.Value = false;
        picture.AllowCompression.Value = false;
        picture.MaxSizeOverride.Value = -1;
        picture.URL.Value = new Uri("res://Assets/Textures/Icons/settings.svg");
        var image = glyph.AttachComponent<Image>();
        image.Texture.Target = picture;
        image.Tint.Value = color.White;
        if (!_settingsOpen) return;

        var popup = _contentSlot.AddSlot("Settings popover");
        BottomCentreRect(popup, CanvasWidth, 312f, 84f);
        var background = popup.AttachComponent<RoundedPanel>();
        background.Color.Value = DashTheme.GlassBackdrop;
        background.CornerRadius.Value = DashTheme.RadiusPanel;
        background.OutlineColor.Value = DashTheme.OutlineStrong;
        background.OutlineThickness.Value = DashTheme.OutlineWidth;
        var contents = popup.AddSlot("Buttons");
        FillRect(contents);
        contents.AttachComponent<GraphicChunkRoot>();
        ui = Builder(contents);
        var column = ui.VerticalLayout(8f, 16f);
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;
        FillRect(ui.Current);
        int pixels = mirror.LocalResolution > 0 ? mirror.LocalResolution : mirror.Resolution.Value;
        float distance = mirror.LocalRenderDistance ?? mirror.OverrideFarClip.Value ?? 40f;
        Label(ui, "Max resolution");
        ui.PushStyle().MinHeight(56f).PreferredHeight(56f);
        ui.HorizontalLayout(8f);
        Choice(ui, "1K", OnQualityBalanced, pixels <= 1024);
        Choice(ui, "2K", OnQualityHigh, pixels > 1024 && pixels <= 2048);
        Choice(ui, "4K", OnQualityUltra, pixels > 2048 && pixels <= 4096);
        Choice(ui, "8K", OnQualityMaximum, pixels > 4096);
        ui.NestOut();
        ui.PopStyle();
        Label(ui, "Distance");
        ui.PushStyle().MinHeight(56f).PreferredHeight(56f);
        ui.HorizontalLayout(8f);
        Choice(ui, "10 m", OnDistanceNear, distance <= 10f);
        Choice(ui, "40 m", OnDistanceRoom, distance > 10f && distance <= 40f);
        Choice(ui, "100 m", OnDistanceFar, distance > 40f);
        ui.NestOut();
        ui.PopStyle();
        Label(ui, "Aspect ratio");
        ui.PushStyle().MinHeight(56f).PreferredHeight(56f);
        ui.HorizontalLayout(8f);
        float ratio = Size.Value.x / MathF.Max(.001f, Size.Value.y);
        Choice(ui, "1:1", OnAspectSquare, MathF.Abs(ratio - 1f) < .01f);
        Choice(ui, "4:3", OnAspectStandard, MathF.Abs(ratio - 4f / 3f) < .01f);
        Choice(ui, "16:9", OnAspectWide, MathF.Abs(ratio - 16f / 9f) < .01f);
        Choice(ui, "9:16", OnAspectPortrait, MathF.Abs(ratio - 9f / 16f) < .01f);
    }

    private UIBuilder Builder(Slot slot)
    {
        var ui = new UIBuilder(slot);
        ui.Font(_font).FontSize(22f).TextColor(DashTheme.Text).ForegroundColor(DashTheme.Accent)
            .BackgroundColor(DashTheme.Surface).CornerRadius(DashTheme.RadiusControl);
        return ui;
    }

    private static void Label(UIBuilder ui, string label)
    {
        ui.PushStyle().FontSize(18f).TextColor(DashTheme.TextDim).MinHeight(24f).PreferredHeight(24f);
        ui.Text(label);
        ui.PopStyle();
    }

    private static void Choice(UIBuilder ui, string label, Action<Button, UIInteractionContext> pressed, bool selected)
    {
        ui.PushStyle().FlexibleWidth(1f);
        ui.Button(label, pressed, selected ? DashTheme.AccentSoft : DashTheme.Surface);
        ui.PopStyle();
    }

    public void OnSettingsPressed(Button button, UIInteractionContext context)
    {
        _settingsOpen = !_settingsOpen;
        _refreshControls = true;
    }

    public void OnPowerPressed(Button button, UIInteractionContext context)
    {
        if (Portal.Target is { } mirror) mirror.LocalRenderingEnabled = !mirror.LocalRenderingEnabled;
        _refreshControls = true;
    }

    // Retain saved bindings for the earlier presets.
    public void OnQualityLow(Button button, UIInteractionContext context) => SetResolution(512);
    public void OnQualityBalanced(Button button, UIInteractionContext context) => SetResolution(1024);
    public void OnQualityHigh(Button button, UIInteractionContext context) => SetResolution(2048);
    public void OnQualityUltra(Button button, UIInteractionContext context) => SetResolution(4096);
    public void OnQualityMaximum(Button button, UIInteractionContext context) => SetResolution(8192);
    public void OnDistanceNear(Button button, UIInteractionContext context) => SetDistance(10f);
    public void OnDistanceRoom(Button button, UIInteractionContext context) => SetDistance(40f);
    public void OnDistanceFar(Button button, UIInteractionContext context) => SetDistance(100f);
    public void OnAspectSquare(Button button, UIInteractionContext context) => SetAspect(1f);
    public void OnAspectStandard(Button button, UIInteractionContext context) => SetAspect(4f / 3f);
    public void OnAspectWide(Button button, UIInteractionContext context) => SetAspect(16f / 9f);
    public void OnAspectPortrait(Button button, UIInteractionContext context) => SetAspect(9f / 16f);

    private void SetAspect(float ratio)
    {
        // Preserve the longest side so changing shape does not keep growing the panel.
        float span = MathF.Max(Size.Value.x, Size.Value.y);
        Size.Value = ratio >= 1f ? new float2(span, span / ratio) : new float2(span * ratio, span);
        Layout();
        _refreshControls = true;
    }

    private void SetResolution(int pixels)
    {
        if (Portal.Target is { } mirror)
            mirror.LocalResolution = System.Math.Clamp(pixels, Mirror.MinimumResolution, Mirror.MaximumResolution);
        _refreshControls = true;
    }

    private void SetDistance(float metres)
    {
        if (Portal.Target is { } mirror) mirror.LocalRenderDistance = System.Math.Clamp(metres, 1f, 200f);
        _refreshControls = true;
    }

    public void HandleInspectorAction(string argument)
    {
        if (string.IsNullOrEmpty(argument)) return;
        if (argument == "power") OnPowerPressed(null!, default);
        else if (argument == "settings") OnSettingsPressed(null!, default);
        else if (argument[0] == 'q' && int.TryParse(argument.AsSpan(1), out int pixels)) SetResolution(pixels);
        else if (argument[0] == 'd' && int.TryParse(argument.AsSpan(1), out int metres)) SetDistance(metres);
    }

    private static void BottomCentreRect(Slot slot, float width, float height, float bottom)
    {
        var rect = slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = rect.AnchorMax.Value = new float2(.5f, 0f);
        rect.OffsetMin.Value = new float2(-width * .5f, bottom);
        rect.OffsetMax.Value = new float2(width * .5f, bottom + height);
    }

    private static void FillRect(Slot slot)
    {
        var rect = slot.GetOrAttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = rect.OffsetMax.Value = float2.Zero;
    }
}
