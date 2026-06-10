// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class UserspaceDashboard : UIComponent
{
    private const int CaptureHeight = 720;
    // Render the offscreen texture at this multiple of the logical canvas size, then
    // downsample on display. 2x means text/edges land between display pixels with proper
    // detail to filter from instead of stretching 1:1+ from a 1280x720 buffer. - xlinka
    private const int SupersampleScale = 2;
    private const float CanvasScale = 0.001f;
    private const float CaptureDistance = 1f;
    private static readonly float3 RigWorldPosition = new float3(0f, 1000f, 0f);

    private int _captureWidth = 1280;

    public readonly Sync<bool> IsOpen;
    public readonly Sync<float> Distance;
    public readonly Sync<float> VerticalOffset;
    public readonly Sync<float> DisplayHeight;
    public readonly Sync<bool> FollowViewWhileOpen;
    public readonly AssetRef<FontSet> Font;

    private Slot? _renderRig;
    private Slot? _canvasSlot;
    private Slot? _surfaceSlot;
    private Dashboard? _dashboard;
    private RenderTextureProvider? _renderTexture;
    private CurvedPlaneMesh? _displayMesh;
    private UnlitMaterial? _displayMaterial;
    private FontProvider? _fontProvider;
    private bool _built;
    private bool _defaultScreensBuilt;

    public Dashboard? Dashboard => _dashboard;

    public RenderTextureProvider? RenderTextureSource => _renderTexture;

    public UserspaceDashboard()
    {
        IsOpen = new Sync<bool>(this, false);
        Distance = new Sync<float>(this, 1.2f);
        VerticalOffset = new Sync<float>(this, 0f);
        DisplayHeight = new Sync<float>(this, 0.85f);
        FollowViewWhileOpen = new Sync<bool>(this, true);
        Font = new AssetRef<FontSet>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
        ApplyOpenState();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        EnsureBuilt();
        ApplyOpenState();

        var input = Engine.Current?.InputInterface;
        if (input != null)
            input.IsDashboardOpen = IsOpen.Value;

        bool vr = input?.VR_Active ?? false;
        if (_surfaceSlot != null)
            _surfaceSlot.ActiveSelf.Value = vr;

        if (vr && IsOpen.Value && FollowViewWhileOpen.Value)
        {
            PositionInFrontOfFocusedView();
        }
    }

    public override void OnDestroy()
    {
        var input = Engine.Current?.InputInterface;
        if (input != null)
            input.IsDashboardOpen = false;

        if (_renderRig != null && !_renderRig.IsDestroyed)
        {
            _renderRig.Destroy();
        }
        _renderRig = null;
        base.OnDestroy();
    }

    public void Open()
    {
        IsOpen.Value = true;
        SetDashboardOpenFlag(true);
        EnsureBuilt();
        ApplyOpenState();
        PositionInFrontOfFocusedView();
    }

    public void Close()
    {
        IsOpen.Value = false;
        SetDashboardOpenFlag(false);
        ApplyOpenState();
    }

    private static void SetDashboardOpenFlag(bool value)
    {
        var input = Engine.Current?.InputInterface;
        if (input != null)
            input.IsDashboardOpen = value;
    }

    public void Toggle()
    {
        if (IsOpen.Value) Close();
        else Open();
    }

    public void SetAspect(float aspect)
    {
        if (aspect <= 0.1f) return;
        int w = (int)System.MathF.Round(CaptureHeight * aspect);
        if (w < 640) w = 640;
        if (w > 2560) w = 2560;
        if (w == _captureWidth) return;

        _captureWidth = w;
        if (_dashboard != null) _dashboard.Size.Value = new float2(w, CaptureHeight);
        if (_renderTexture != null) _renderTexture.Width.Value = w * SupersampleScale;
        if (_displayMesh != null)
            _displayMesh.Size.Value = new float2(DisplayHeight.Value * ((float)w / CaptureHeight), DisplayHeight.Value);
    }

    public void UpdatePointer(float2 normalized, bool pressed)
    {
        if (_canvasSlot == null) return;
        var canvas = _canvasSlot.GetComponent<Canvas>();
        if (canvas == null) return;

        float lx = (normalized.x - 0.5f) * _captureWidth;
        float ly = (0.5f - normalized.y) * CaptureHeight;
        var worldPoint = _canvasSlot.LocalPointToGlobal(new float3(lx, ly, 0f));
        var forward = _canvasSlot.Forward;
        canvas.UpdatePointer(UIInteractionSource.Desktop, 0, worldPoint + forward * 0.5f, -forward, pressed, World?.LocalUser);
    }

    public void ClearPointer()
    {
        var canvas = _canvasSlot?.GetComponent<Canvas>();
        canvas?.ClearPointer(UIInteractionSource.Desktop, 0, World?.LocalUser);
    }

    public bool FeedAxis(float2 axis)
    {
        var canvas = _canvasSlot?.GetComponent<Canvas>();
        if (canvas == null) return false;
        return canvas.ProcessAxis(UIInteractionSource.Desktop, 0, in axis);
    }

    private string _searchBuffer = string.Empty;

    public void FeedSearchChar(char c)
    {
        if (_dashboard?.CurrentScreen is FileBrowserScreen fb && fb.ConsumeChar(c))
            return;
        _searchBuffer += c;
        ApplySearch();
    }

    public void FeedSearchBackspace()
    {
        if (_dashboard?.CurrentScreen is FileBrowserScreen fb && fb.ConsumeBackspace())
            return;
        if (_searchBuffer.Length == 0) return;
        _searchBuffer = _searchBuffer.Substring(0, _searchBuffer.Length - 1);
        ApplySearch();
    }

    public bool FeedEnter()
    {
        return _dashboard?.CurrentScreen is FileBrowserScreen fb && fb.ConsumeEnter();
    }

    public bool FeedEscape()
    {
        return _dashboard?.CurrentScreen is FileBrowserScreen fb && fb.ConsumeEscape();
    }

    public void ClearSearch()
    {
        if (_searchBuffer.Length == 0) return;
        _searchBuffer = string.Empty;
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (_dashboard?.CurrentScreen is FileBrowserScreen fb)
            fb.SetSearch(_searchBuffer);
    }

    public void UpdateVrPointer(InteractionLaser laser, int pointerId, float2 normalized, bool pressed)
    {
        if (_canvasSlot == null) return;
        var canvas = _canvasSlot.GetComponent<Canvas>();
        if (canvas == null) return;

        float lx = (normalized.x - 0.5f) * _captureWidth;
        float ly = (0.5f - normalized.y) * CaptureHeight;
        var worldPoint = _canvasSlot.LocalPointToGlobal(new float3(lx, ly, 0f));
        var forward = _canvasSlot.Forward;
        canvas.UpdatePointer(VrSource(laser), pointerId, worldPoint + forward * 0.5f, -forward, pressed, World?.LocalUser);
    }

    public void ClearVrPointer(InteractionLaser laser, int pointerId)
    {
        var canvas = _canvasSlot?.GetComponent<Canvas>();
        canvas?.ClearPointer(VrSource(laser), pointerId, World?.LocalUser);
    }

    private static UIInteractionSource VrSource(InteractionLaser laser)
    {
        return laser?.ControllerSide.Value == Chirality.Left
            ? UIInteractionSource.VRLeft
            : UIInteractionSource.VRRight;
    }

    private void EnsureBuilt()
    {
        EnsureFont();
        BuildRenderRig();
        BuildDisplaySurface();
        BuildDefaultScreens();
    }

    private static readonly Uri DefaultFontUri = new("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

    private void EnsureFont()
    {
        // Register the font URL so import dialogs can create their own
        // FontProviders in their own world (avoids cross-world AssetRef
        // rejection). Sharing a FontProvider component instance across
        // worlds doesn't work — SyncRef.Target rejects it. - xlinka
        ImportDialog.DefaultFontUrl ??= DefaultFontUri;

        if (Font.Target != null) return;

        _fontProvider ??= Slot.FindChild("UIFont", recursive: false)?.GetComponent<FontProvider>();
        if (_fontProvider == null)
        {
            var fontSlot = Slot.AddSlot("UIFont");
            _fontProvider = fontSlot.AttachComponent<FontProvider>();
            _fontProvider.URL.Value = DefaultFontUri;
            _fontProvider.FallbackURLs.Add(DefaultFontUri);
        }

        Font.Target = _fontProvider;
    }

    private void BuildRenderRig()
    {
        if (_renderRig != null && !_renderRig.IsDestroyed) return;

        var parent = Slot.Parent ?? World.RootSlot;
        _renderRig = parent.AddSlot("DashRenderRig");
        _renderRig.Persistent.Value = false;
        _renderRig.GlobalPosition = RigWorldPosition;
        _renderRig.GlobalRotation = floatQ.Identity;

        var layerOverride = _renderRig.AttachComponent<RenderLayerOverride>();
        layerOverride.Layer.Value = RenderLayerOverride.HiddenLayer;

        _canvasSlot = _renderRig.AddSlot("DashCanvas");
        _canvasSlot.LocalScale.Value = float3.One * CanvasScale;

        _dashboard = _canvasSlot.AttachComponent<Dashboard>();
        _dashboard.Size.Value = new float2(_captureWidth, CaptureHeight);
        _dashboard.Font.Target = Font.Target;

        _renderTexture = _renderRig.AttachComponent<RenderTextureProvider>();
        _renderTexture.Width.Value = _captureWidth * SupersampleScale;
        _renderTexture.Height.Value = CaptureHeight * SupersampleScale;
        _renderTexture.CullMask.Value = RenderLayerOverride.HiddenLayer;
        _renderTexture.OrthographicSize.Value = CaptureHeight * CanvasScale;
        _renderTexture.CameraPosition.Value = RigWorldPosition + new float3(0f, 0f, CaptureDistance);
        _renderTexture.CameraRotation.Value = floatQ.Identity;
    }

    private void BuildDisplaySurface()
    {
        if (_built) return;
        if (_renderTexture == null) return;
        _built = true;

        float aspect = (float)_captureWidth / CaptureHeight;
        float height = DisplayHeight.Value;

        _surfaceSlot = Slot.AddSlot("Surface");
        _surfaceSlot.ActiveSelf.Value = false;

        _displayMesh = _surfaceSlot.AttachComponent<CurvedPlaneMesh>();
        _displayMesh.Size.Value = new float2(height * aspect, height);
        _displayMesh.Curvature.Value = 0.5f;
        _displayMesh.Segments.Value = 24;

        _displayMaterial = _surfaceSlot.AttachComponent<UnlitMaterial>();
        _displayMaterial.BlendMode.Value = BlendMode.Alpha;
        _displayMaterial.Culling.Value = Culling.None;
        _displayMaterial.TintColor.Value = colorHDR.White;
        _displayMaterial.Texture.Target = _renderTexture;

        var renderer = _surfaceSlot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _displayMesh;
        renderer.Material.Target = _displayMaterial;

        _surfaceSlot.AttachComponent<DashSurfacePortal>();
    }

    private void BuildDefaultScreens()
    {
        if (_defaultScreensBuilt || _dashboard == null) return;
        _defaultScreensBuilt = true;

        AddInfoScreen("Home", new color(0.94f, 0.81f, 0f, 1f),
            "Your home space and quick actions.");
        AddInfoScreen("Worlds", new color(0.20f, 0.80f, 1f, 1f),
            "Browse, open and manage worlds.");
        AddInfoScreen("Session", new color(0.25f, 0.55f, 1f, 1f),
            "Session controls and the users in this session.");
        AddInfoScreen("Settings", new color(1f, 0.22f, 0.28f, 1f),
            "Interface, input, audio and locomotion settings.");
        AddInfoScreen("Friends", new color(0.30f, 1f, 0.80f, 1f),
            "Friends, requests and messages.");
        AddInfoScreen("Inventory", new color(0.85f, 0.60f, 0.22f, 1f),
            "Your saved items and avatars.");
        _dashboard?.AddScreen<FileBrowserScreen>("Files", new color(0.70f, 0.66f, 0.32f, 1f));
    }

    private void AddInfoScreen(string label, color accent, string body)
    {
        if (_dashboard == null) return;

        var screen = _dashboard.AddScreen<DashboardScreen>(label, accent);
        var content = screen.ContentSlot;
        if (content == null) return;

        var builder = new UIBuilder(content);
        builder.Font(Font.Target).TextColor(color.White).BackgroundColor(new color(0.06f, 0.07f, 0.09f, 0.82f));
        var layout = builder.VerticalLayout(12f, 22f);
        Fill(layout.RectTransform!);

        builder.FontSize(18f).MinHeight(120f).PreferredHeight(160f).FlexibleHeight(0f);
        var text = builder.Text(body, 18f, new color(0.82f, 0.87f, 0.92f, 1f));
        text.VerticalAlignment.Value = TextVerticalAlignment.Top;
        Fill(text.RectTransform!);

        builder.NestOut();
    }

    private void ApplyOpenState()
    {
        Slot.ActiveSelf.Value = IsOpen.Value;
    }

    private void PositionInFrontOfFocusedView()
    {
        if (!TryGetFocusedViewPose(out var headPosition, out var headRotation))
        {
            Slot.LocalPosition.Value = new float3(0f, 1.45f, -Distance.Value);
            Slot.LocalRotation.Value = floatQ.Identity;
            Slot.LocalScale.Value = float3.One;
            return;
        }

        var localOffset = float3.Forward * Distance.Value + float3.Down * VerticalOffset.Value;
        Slot.GlobalPosition = headPosition + headRotation * localOffset;
        Slot.GlobalRotation = headRotation;
        Slot.LocalScale.Value = float3.One;
    }

    private static bool TryGetFocusedViewPose(out float3 headPosition, out floatQ headRotation)
    {
        var root = Engine.Current?.WorldManager?.FocusedWorld?.LocalUser?.Root;
        if (root?.HeadSlot != null)
        {
            headPosition = root.HeadPosition;
            headRotation = root.HeadRotation;
            return true;
        }

        headPosition = new float3(0f, 1.6f, 0f);
        headRotation = floatQ.Identity;
        return false;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
