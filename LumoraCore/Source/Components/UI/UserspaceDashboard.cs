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

    // Reserved high render band for the dash display surface. Draw order is SortingOffset = material
    // RenderQueue + renderer SortingOrder (uncapped). World UI/lasers/context menus sit at ~4000-10000, so
    // 20000 keeps the dash above all of them; the laser cursor sits one band above (InteractionLaser) so the
    // pointer stays visible on the dash. -xlinka
    private const int DashSurfaceSortingOrder = 20000;
    private static readonly float3 RigWorldPosition = new float3(0f, 1000f, 0f);

    private int _captureWidth = 1280;

    public readonly Sync<bool> IsOpen;
    public readonly Sync<float> Distance;
    public readonly Sync<float> VerticalOffset;
    public readonly Sync<float> DisplayHeight;
    public readonly Sync<bool> FollowViewWhileOpen;
    // Freeform: in VR, stop snapping the panel in front of the view every frame.
    // Instead it stays where it was last placed (snapped in front once when you
    // open it or flip freeform on) and a grab handle lets you reposition it.
    // Desktop is always window-projected, so freeform only affects VR.
    public readonly Sync<bool> Freeform;
    public readonly AssetRef<FontSet> Font;

    private Slot? _renderRig;
    private Slot? _canvasSlot;
    private Slot? _surfaceSlot;
    private Dashboard? _dashboard;
    private RenderTextureProvider? _renderTexture;
    private CurvedPlaneMesh? _displayMesh;
    private UIUnlitMaterial? _displayMaterial;
    private FontProvider? _fontProvider;
    private Grabbable? _grabHandle;
    private bool _lastFreeform;
    private bool _wasOpen;
    // Last seen shared-font atlas generation. New glyphs rasterize on demand and bump this; we re-render the
    // viewport when it changes so freshly-seen text isn't left blank under render-on-dirty. -xlinka
    private int _lastFontGeneration = -1;
    private bool _built;
    private bool _defaultScreensBuilt;

    public Dashboard? Dashboard => _dashboard;

    public RenderTextureProvider? RenderTextureSource => _renderTexture;

    /// <summary>The display surface slot; lasers are restricted to it while the dash is open on desktop.</summary>
    public Slot? SurfaceSlot => _surfaceSlot != null && !_surfaceSlot.IsDestroyed ? _surfaceSlot : null;

    /// <summary>The local user's dashboard, if built.</summary>
    public static UserspaceDashboard? LocalInstance { get; private set; }

    public UserspaceDashboard()
    {
        IsOpen = new Sync<bool>(this, false);
        Distance = new Sync<float>(this, 1.2f);
        VerticalOffset = new Sync<float>(this, 0f);
        DisplayHeight = new Sync<float>(this, 0.85f);
        FollowViewWhileOpen = new Sync<bool>(this, true);
        Freeform = new Sync<bool>(this, false);
        Font = new AssetRef<FontSet>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        LocalInstance = this;
        EnsureBuilt();
        ApplyOpenState();
        ScheduleRigParkAfterWarmup();
    }

    // Parking the rig before the canvas has ever built means the first open shows
    // the UI assembling piece by piece (layout, then glyphs as the atlas rasterizes).
    // Let it warm up once at startup, then park while closed.
    private bool _rigWarmed;

    private void ScheduleRigParkAfterWarmup()
    {
        void Check()
        {
            if (IsDestroyed) return;
            var canvas = _canvasSlot?.GetComponent<Canvas>();
            if (canvas?.RootChunk != null)
            {
                // One more breath so glyph-atlas re-renders land before parking.
                World?.RunInUpdates(120, () =>
                {
                    if (IsDestroyed) return;
                    _rigWarmed = true;
                    ApplyOpenState();
                });
            }
            else
            {
                World?.RunInUpdates(30, Check);
            }
        }
        World?.RunInUpdates(30, Check);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        EnsureBuilt();
        ApplyOpenState();

        var input = Engine.Current?.InputInterface;
        if (input != null)
            input.IsDashboardOpen = IsOpen.Value;

        // One dash surface for both modes: curved world-space panel in VR,
        // flattened and projection-fitted to the window on desktop. The free
        // cursor steers the laser over it, so the laser dot is the one pointer.
        bool open = IsOpen.Value;
        bool vr = input?.VR_Active ?? false;
        if (_surfaceSlot != null)
            _surfaceSlot.ActiveSelf.Value = open;

        if (_displayMesh != null)
            _displayMesh.Curvature.Value = vr ? 0.5f : 0f;

        // First frame the dash becomes visible, force the current screen to re-render. Reactivating
        // the parked render rig doesn't dirty the canvas on its own, so without this the dash shows
        // blank until you switch tabs (which forces a rebuild). Does what a tab switch does, on open.
        if (open && !_wasOpen)
            _dashboard?.ForceRebuild();
        _wasOpen = open;

        // Render the offscreen UI only when it actually changed. The canvas flags a render after each mesh
        // submit; we pulse the viewport once for it. A static dash keeps showing its last frame at zero GPU
        // cost instead of re-rendering the full-res 2x-supersampled capture every single frame. -xlinka
        if (open)
        {
            var canvas = _canvasSlot?.GetComponent<Canvas>();
            if (canvas != null && canvas.ConsumeRenderRequested())
                _renderTexture?.Asset?.RequestRender();

            // New glyphs rasterize into the shared font atlas on demand; the atlas GPU upload is deferred and
            // the glyph meshes already have correct UVs, so we just need ONE more viewport render after the
            // atlas texture has the new pixels - otherwise newly-seen text stays blank until an unrelated
            // re-dirty (the "text loads late / disappears" bug). The generation bumps at rasterize time and we
            // observe it a frame later, by which point the deferred upload has landed. It's the standard
            // atlas-update -> re-render pattern, minus any async pin/await (our rasterizer is synchronous, so the
            // UVs are already right and a re-render is enough - no re-mesh). -xlinka
            int fontGeneration = Font.Target?.Asset?.CacheGeneration ?? 0;
            if (fontGeneration != _lastFontGeneration)
            {
                _lastFontGeneration = fontGeneration;
                _renderTexture?.Asset?.RequestRender();
            }
        }

        // The whole-surface grab is disabled: boosting it above the canvas to move
        // the dash also stole the laser in edit mode (you'd grab the whole dash
        // instead of dragging a widget) and blocked clicks. Freeform place-and-stay
        // (below) doesn't need it; repositioning should use a dedicated grab handle
        // (future), not the entire surface.
        bool freeform = Freeform.Value;
        if (_grabHandle != null)
        {
            _grabHandle.AllowGrab.Value = false;
            _grabHandle.InteractionPriority.Value = -1;
        }

        if (!open)
        {
            _lastFreeform = freeform;
            return;
        }

        if (vr)
        {
            // Locked: pin the panel in front of the view every frame.
            // Freeform: leave it where it was placed; snap it back in front once
            // at the moment freeform is switched on so it stays within reach.
            if (!freeform)
            {
                if (FollowViewWhileOpen.Value)
                    PositionInFrontOfFocusedView();
            }
            else if (!_lastFreeform)
            {
                PositionInFrontOfFocusedView();
            }
        }
        // Desktop positioning is intentionally NOT done here. OnCommonUpdate runs the userspace world BEFORE the
        // session world each frame, so the head pose would be a frame stale here. It's done in OnLateUpdate
        // (after all worlds' updates, incl. the session locomotion/physics) so it reads the fresh head pose. -xlinka

        _lastFreeform = freeform;
    }

    public override void OnLateUpdate(float delta)
    {
        base.OnLateUpdate(delta);

        // Position the DESKTOP dashboard here, in late update, so it runs AFTER the session world's
        // locomotion/physics has moved the head this frame - the panel then reads the exact pose the camera
        // renders with and stays screen-locked instead of bobbing when you walk/jump. (VR positioning stays in
        // OnCommonUpdate; its head is HMD-driven, not physics-bounced.) -xlinka
        if (!IsOpen.Value)
            return;

        var input = Engine.Current?.InputInterface;
        if (input != null && !input.VR_Active)
            PositionDesktopProjection(input);
    }

    // Fit the flat surface to the window: place it ahead of the camera and scale
    // so it fills the viewport height (clamped by width), like a screen overlay.
    // Positions from the platform-pushed camera pose - the camera looks along
    // its local -Z, and the surface yaws 180 so its readable face points back
    // at the viewer with the same relative geometry as the VR placement.
    private void PositionDesktopProjection(InputInterface? input)
    {
        if (input == null || _displayMesh == null || !input.DesktopCameraPoseValid)
            return;

        // Anchor to the LIVE head pose this frame when the desktop camera is in plain first-person (the camera
        // follows the head, so this is EXACTLY the pose the view renders with). This is called from OnLateUpdate,
        // after the session world's locomotion/physics has moved the head, so it's the same post-move pose the
        // camera uses - zero lag, so walking/jumping no longer makes the panel bob. The old path read the
        // separately-sampled DesktopCameraPosition, which trails the view by a frame; during non-constant
        // locomotion that trailing offset changes every frame -> visible jitter. When a camera OVERRIDE is active
        // (3rd-person / free-cam) the view is NOT the head, so fall back to the sampled camera pose. -xlinka
        float3 camPos;
        floatQ camRot;
        if (!input.DesktopCameraHasOverride && TryGetFocusedViewPose(out var headPos, out var headRot))
        {
            camPos = headPos;
            camRot = headRot;
        }
        else
        {
            camPos = input.DesktopCameraPosition;
            camRot = input.DesktopCameraRotation;
        }

        var forward = camRot * new float3(0f, 0f, -1f);
        float distance = MathF.Max(Distance.Value, 0.2f);

        // Readable face of UI content is the surface's +Z side; with the camera
        // looking down -Z, camRot already points that face at the viewer.
        Slot.GlobalPosition = camPos + forward * distance;
        Slot.GlobalRotation = camRot;

        float viewportHeight = 2f * distance * MathF.Tan(input.DesktopCameraFovY * (MathF.PI / 180f) * 0.5f);

        // Shape the display mesh to the EXACT window aspect (the capture
        // resolution stays quantized for perf - the texture just samples across
        // this quad). With the mesh aspect matching the window, a uniform
        // height-fit fills the screen edge-to-edge with no letterbox bars.
        float exactWidth = DisplayHeight.Value * input.DesktopViewportAspect;
        if (MathF.Abs(_displayMesh.Size.Value.x - exactWidth) > 0.0005f)
            _displayMesh.Size.Value = new float2(exactWidth, DisplayHeight.Value);

        var meshSize = _displayMesh.Size.Value;
        if (meshSize.y <= 0.001f)
            return;

        Slot.LocalScale.Value = float3.One * (viewportHeight / meshSize.y);

        // Keep the capture aspect in sync with the window so content isn't
        // letterboxed inside the surface.
        SetAspect(input.DesktopViewportAspect);
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(LocalInstance, this))
            LocalInstance = null;

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

    /// <summary>Freeform places the panel and leaves it; locked keeps it pinned in front of the view (VR only).</summary>
    public void SetFreeform(bool value) => Freeform.Value = value;

    public void ToggleFreeform() => Freeform.Value = !Freeform.Value;

    public void SetAspect(float aspect)
    {
        if (aspect <= 0.1f) return;
        // Quantize so near-16:9 windows resolve to the default 1280 and small
        // window-size jitter can't trigger full canvas rebuilds.
        int w = (int)System.MathF.Round(CaptureHeight * aspect / 64f) * 64;
        if (w < 640) w = 640;
        if (w > 2560) w = 2560;
        if (w == _captureWidth) return;

        _captureWidth = w;
        if (_dashboard != null) _dashboard.Size.Value = new float2(w, CaptureHeight);
        if (_renderTexture != null) _renderTexture.Width.Value = w * SupersampleScale;
        if (_displayMesh != null)
            _displayMesh.Size.Value = new float2(DisplayHeight.Value * ((float)w / CaptureHeight), DisplayHeight.Value);
    }

    public bool FeedAxis(float2 axis)
    {
        var canvas = _canvasSlot?.GetComponent<Canvas>();
        if (canvas == null) return false;
        // The dashboard hovers under a VR* source (the laser), so a fixed (Desktop,0) feed never matched
        // the hover - route the wheel to whichever pointer is actually over the canvas. -xlinka
        return canvas.ProcessAxisAnyPointer(in axis);
    }

    private string _searchBuffer = string.Empty;

    public void FeedSearchChar(char c)
    {
        // Let the current screen grab the keystroke first (inline name fields etc.); only fall through to the
        // file-browser search buffer if nothing consumed it. -xlinka
        if (_dashboard?.CurrentScreen is IDashboardKeyInput k && k.ConsumeChar(c))
            return;
        _searchBuffer += c;
        ApplySearch();
    }

    public void FeedSearchBackspace()
    {
        if (_dashboard?.CurrentScreen is IDashboardKeyInput k && k.ConsumeBackspace())
            return;
        if (_searchBuffer.Length == 0) return;
        _searchBuffer = _searchBuffer.Substring(0, _searchBuffer.Length - 1);
        ApplySearch();
    }

    public bool FeedEnter()
    {
        return _dashboard?.CurrentScreen is IDashboardKeyInput k && k.ConsumeEnter();
    }

    public bool FeedEscape()
    {
        return _dashboard?.CurrentScreen is IDashboardKeyInput k && k.ConsumeEscape();
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
        EnsureGrabHandle();
    }

    // Freeform grab handle on the dash root: grabbing reparents this slot to the
    // hand (surface + portal ride along) and restores it on release, leaving the
    // panel where you let go. OnCommonUpdate gates AllowGrab to open+VR+freeform.
    private void EnsureGrabHandle()
    {
        if (_grabHandle != null && !_grabHandle.IsDestroyed)
            return;
        _grabHandle = Slot.GetComponent<Grabbable>() ?? Slot.AttachComponent<Grabbable>();
        _grabHandle.AllowGrab.Value = false;
        _grabHandle.Scalable.Value = false;
        _grabHandle.FollowRotation.Value = true;
        _grabHandle.Receivable.Value = false;
        _grabHandle.InteractionPriority.Value = -1;
    }

    private static readonly Uri DefaultFontUri = new("res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf");

    private void EnsureFont()
    {
        // Register the font URL so import dialogs can create their own
        // FontProviders in their own world (avoids cross-world AssetRef
        // rejection). Sharing a FontProvider component instance across
        // worlds doesn't work - SyncRef.Target rejects it. - xlinka
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
        // Opaque capture: alpha-blended UI doesn't accumulate usable alpha in a
        // transparent viewport, which made the whole dash ghostly. The slight
        // see-through look comes from the surface material tint instead.
        _renderTexture.ClearColor.Value = new color(0.06f, 0.05f, 0.11f, 1f);
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

        // UI overlay material with ZTest Always: the base UI shader depth-tests now (panels occlude
        // properly), but the DASH must keep drawing above session-world geometry in desktop + VR.
        _displayMaterial = _surfaceSlot.AttachComponent<UIUnlitMaterial>();
        _displayMaterial.ZTest.Value = ZTest.Always;
        _displayMaterial.Culling.Value = Culling.None;
        _displayMaterial.UseVertexColor.Value = false;
        // Fully opaque: the capture (its own SubViewport, opaque dark clear, no skybox) is the dash's
        // isolation - it renders to its own pass, not into the session world. Any surface translucency just lets
        // the session skybox/sun bleed through the dash, which is what we're avoiding. - xlinka
        _displayMaterial.TintColor.Value = new colorHDR(1f, 1f, 1f, 1f);
        _displayMaterial.RenderQueue.Value = 4002;
        _displayMaterial.Texture.Target = _renderTexture;

        var renderer = _surfaceSlot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _displayMesh;
        renderer.Material.Target = _displayMaterial;
        // Draw order is SortingOffset = material RenderQueue + renderer SortingOrder (uncapped), NOT the
        // Godot-clamped render_priority. With SortingOrder 0 the dash sat at ~4002, BELOW every laser visual
        // (~4100), the context menu (~10000) and any other session overlay - so they drew over it. Put the
        // dash in a high reserved band (offset ~24002) so nothing in the session world covers it; the laser
        // cursor sits one band above (see InteractionLaser) so the pointer stays visible on the dash. -xlinka
        renderer.SortingOrder.Value = DashSurfaceSortingOrder;

        _surfaceSlot.AttachComponent<DashSurfacePortal>();
    }

    private void BuildDefaultScreens()
    {
        if (_defaultScreensBuilt || _dashboard == null) return;
        _defaultScreensBuilt = true;

        // Tab order: Home (with the Create New World widget), Worlds (the world/session
        // browser), then the social cluster (Friends/Groups/Inventory), Session, Settings,
        // with the file browser last.
        _dashboard?.AddScreen<HomeScreen>("Home", new color(0.94f, 0.81f, 0f, 1f));
        _dashboard?.AddScreen<WorldsScreen>("Worlds", new color(0.20f, 0.80f, 1f, 1f));
        // No friends/contacts backend exists yet, so present this honestly as a placeholder rather
        // than a working social feature: a dimmed "(Soon)" tab whose body says it isn't available. -xlinka
        AddComingSoonScreen("Friends", new color(0.30f, 1f, 0.80f, 1f),
            "A friends and direct-message system isn't available yet.");
        _dashboard?.AddScreen<GroupsScreen>("Groups", new color(0.55f, 0.45f, 0.95f, 1f));
        _dashboard?.AddScreen<InventoryScreen>("Inventory", new color(0.85f, 0.60f, 0.22f, 1f));
        _dashboard?.AddScreen<SessionScreen>("Session", new color(0.25f, 0.55f, 1f, 1f));
        _dashboard?.AddScreen<SettingsScreen>("Settings", new color(1f, 0.22f, 0.28f, 1f));
        _dashboard?.AddScreen<FileBrowserScreen>("Files", new color(0.70f, 0.66f, 0.32f, 1f));
        _dashboard?.AddScreen<DebugScreen>("Debug", new color(0.45f, 0.78f, 0.55f, 1f));
        _dashboard?.AddScreen<ExitScreen>("Exit", new color(1f, 0.30f, 0.32f, 1f));
    }

    // An honest placeholder tab for a feature with no backend yet: a "(Soon)" nav label and a body
    // that plainly states it isn't available, so it never reads as a working feature. -xlinka
    private void AddComingSoonScreen(string label, color accent, string body)
    {
        if (_dashboard == null) return;

        var screen = _dashboard.AddScreen<DashboardScreen>(label + " (Soon)", accent);
        var content = screen.ContentSlot;
        if (content == null) return;

        var builder = new UIBuilder(content);
        builder.Font(Font.Target).TextColor(color.White).BackgroundColor(new color(0.06f, 0.07f, 0.09f, 0.82f));
        var layout = builder.VerticalLayout(10f, 28f);
        Fill(layout.RectTransform!);

        builder.FontSize(24f).MinHeight(44f).PreferredHeight(48f).FlexibleHeight(0f);
        var heading = builder.Text($"{label} - coming soon", 24f, new color(0.62f, 0.66f, 0.74f, 1f));
        heading.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        heading.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        builder.FontSize(16f).MinHeight(60f).PreferredHeight(90f).FlexibleHeight(0f);
        var text = builder.Text(body, 16f, new color(0.62f, 0.66f, 0.74f, 1f));
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Top;

        builder.NestOut();
    }

    private void ApplyOpenState()
    {
        bool open = IsOpen.Value;
        Slot.ActiveSelf.Value = open;

        // The render rig lives beside this slot, not under it, so closing the dash
        // doesn't park it for free. Deactivate it (stops canvas rebuilds, widget
        // drivers, mesh uploads) and pause the offscreen viewport render, otherwise
        // a closed dash keeps re-rendering at full supersampled resolution forever.
        // While warming up at startup the rig stays on regardless of open state.
        bool runRig = open || !_rigWarmed;
        if (_renderRig != null && !_renderRig.IsDestroyed)
            _renderRig.ActiveSelf.Value = runRig;
        _renderTexture?.Asset?.SetRenderEnabled(runRig);
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
