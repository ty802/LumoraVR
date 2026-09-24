// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Godot.Helpers;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// Each eye renders through the mirror's physical rectangle using an off-axis frustum.
// This keeps the clip plane on the glass and fills a fixed-resolution texture at every distance.
// Older CameraPortal surfaces retain the screen-space sampling path below.
[ImplementableHook(typeof(CameraPortal), typeof(Lumora.Core.Components.Mirror))]
public sealed partial class CameraPortalHook : ComponentHook<CameraPortal>
{
    private const int MaxViews = 2;
    private const int MinEyeHeight = 64;
    private const int MaxEyeHeight = Lumora.Core.Components.Mirror.MaximumResolution;
    // Fraction of the exact plane depth the near plane is placed at: everything closer is clipped,
    // and the plane itself keeps a margin so its own edge pixels are never cut.
    private const float NearPlaneFraction = 0.97f;
    private const int ShadowAtlas = 1024;
    // Texture pixels per screen pixel the glass is rendered at. A little over one so the bilinear
    // resample of the reflection never softens it below the eye's own sharpness.
    private const float MirrorDensity = 1.25f;
    // Render targets are reallocated on every size change, so sizes move in steps and shrink late.
    private const int SizeStep = 64;
    private const int ShrinkHysteresis = 128;

    private PortalProcessNode _processNode = null!;
    private readonly SubViewport?[] _viewports = new SubViewport?[MaxViews];
    private readonly Camera3D?[] _cameras = new Camera3D?[MaxViews];
    private readonly int[] _settled = new int[MaxViews];
    private static readonly List<SubViewport> _liveViewports = new();
    private static readonly object _liveViewportLock = new();
    public static void CollectLiveViewports(List<SubViewport> into)
    {
        lock (_liveViewportLock)
        {
            for (int i = _liveViewports.Count - 1; i >= 0; i--)
            {
                var viewport = _liveViewports[i];
                if (viewport == null || !GodotObject.IsInstanceValid(viewport))
                {
                    _liveViewports.RemoveAt(i);
                    continue;
                }
                into.Add(viewport);
            }
        }
    }
    private RenderTextureHook? _boundTexture;
    private global::Godot.Environment? _clearEnvironment;
    private int _publishedViews = -1;
    private bool _parked = true;
    private bool _loggedNoViewer;
    private Lumora.Core.Assets.RenderTexture? _boundAsset;
    private Texture2D? _offTexture;
    private global::Godot.Environment? _sourceEnvironment;
    private global::Godot.Environment? _captureEnvironment;
    private bool _environmentDirty = true;

    public override void Initialize()
    {
        base.Initialize();
        _processNode = new PortalProcessNode(this) { Name = "CameraPortal" };
        attachedNode.AddChild(_processNode);
    }

    // Everything here is per frame; a changed member is picked up on the next tick.
    public override void ApplyChanges() { }

    public override void Destroy(bool destroyingWorld)
    {
        // Viewports are hosted outside the owning world, so world teardown must free them too.
        {
            ReleaseTexture();
            for (int i = 0; i < MaxViews; i++)
            {
                var viewport = _viewports[i];
                if (viewport != null && GodotObject.IsInstanceValid(viewport))
                    viewport.QueueFree();
                _viewports[i] = null;
                _cameras[i] = null;
            }
            _clearEnvironment?.Dispose();
            _clearEnvironment = null;
            if (_sourceEnvironment != null && GodotObject.IsInstanceValid(_sourceEnvironment))
                _sourceEnvironment.Changed -= EnvironmentChanged;
            _sourceEnvironment = null;
            _captureEnvironment?.Dispose();
            _captureEnvironment = null;
            _offTexture?.Dispose();
            _offTexture = null;
            if (_processNode != null && GodotObject.IsInstanceValid(_processNode))
                _processNode.QueueFree();
        }
        _processNode = null!;
        base.Destroy(destroyingWorld);
    }

    internal void Tick()
    {
        var owner = Owner;
        if (owner == null || owner.IsDestroyed || owner.Slot == null)
        {
            Park();
            return;
        }

        var textureHook = owner.ReflectionTexture.Target?.Asset?.Hook as RenderTextureHook;
        if (!ReferenceEquals(textureHook, _boundTexture))
        {
            ReleaseTexture();
            _boundTexture = textureHook;
            _boundAsset = owner.ReflectionTexture.Target?.Asset;
            _publishedViews = -1;
            _parked = false;
        }

        if (!owner.Enabled || !owner.LocalRenderingEnabled || textureHook == null || !owner.Slot.IsActive
            || !owner.World.IsFocused)
        {
            Park();
            return;
        }

        var viewer = RenderHelper.ViewerCamera?.Invoke() ?? _processNode.GetViewport()?.GetCamera3D();
        if (viewer == null || !GodotObject.IsInstanceValid(viewer) || !viewer.IsInsideTree())
        {
            if (!_loggedNoViewer)
            {
                _loggedNoViewer = true;
                LumoraLogger.Warn($"CameraPortal '{owner.Slot.SlotName.Value}': no viewer camera to render from.");
            }
            Park();
            return;
        }
        var viewerViewport = viewer.GetViewport();
        if (viewerViewport == null)
        {
            Park();
            return;
        }

        // The plane, off the node this hook owns: current for this frame, whatever moved the slot.
        var portalXf = attachedNode.GlobalTransform;
        var localNormal = owner.PlaneNormal.Value;
        if (localNormal.LengthSquared < 1e-8f)
            localNormal = Lumora.Core.Math.float3.Backward;
        var planeNormal = (portalXf.Basis * new Vector3(localNormal.x, localNormal.y, localNormal.z)).Normalized();
        var planePos = portalXf.Origin + planeNormal * owner.PlaneOffset.Value;
        var glassPos = planePos;

        bool portal = owner.RenderMode.Value == CameraPortal.Mode.Portal;
        var portalMap = Transform3D.Identity;
        if (portal)
        {
            var target = owner.PortalTarget.Target;
            if (target == null || target.IsDestroyed)
            {
                Park();
                return;
            }
            var tp = target.GlobalPosition;
            var tr = target.GlobalRotation;
            var targetXf = new Transform3D(new Basis(new Quaternion(tr.x, tr.y, tr.z, tr.w)), new Vector3(tp.x, tp.y, tp.z));
            var here = new Transform3D(portalXf.Basis.Orthonormalized(), portalXf.Origin);
            portalMap = targetXf * here.AffineInverse();
            // The clip plane for a portal is the far end: what is between the mapped camera and the
            // target's surface is behind the target, not in the picture.
            planeNormal = (targetXf.Basis * new Vector3(localNormal.x, localNormal.y, localNormal.z)).Normalized();
            planePos = targetXf.Origin + planeNormal * owner.PlaneOffset.Value;
        }

        var viewerPos = viewer.GlobalTransform.Origin;
        if (owner.ActivationDistance.Value > 0f
            && viewerPos.DistanceTo(planePos) > owner.ActivationDistance.Value)
        {
            Park();
            return;
        }
        if (!portal && (viewerPos - planePos).Dot(planeNormal) <= 0f)
        {
            // Behind the glass there is nothing to reflect, and nothing to pay for.
            Park();
            return;
        }

        var xr = XRServer.PrimaryInterface;
        bool stereo = viewerViewport.UseXR && xr != null && xr.IsInitialized() && xr.GetViewCount() >= 2;
        int views = stereo ? 2 : 1;
        bool surfaceMirror = !portal && owner.SurfaceSize.Value.x > 0f && owner.SurfaceSize.Value.y > 0f;

        // Per-view transforms are relative to the XR origin, which is the camera's parent.
        var originXf = viewer.GetParent() is Node3D origin ? origin.GlobalTransform : Transform3D.Identity;
        var viewerSize = viewerViewport.GetVisibleRect().Size;
        float viewerAspect = viewerSize.Y > 0f ? viewerSize.X / viewerSize.Y : 1f;

        var host = WorldManagerHook.Instance?.Root;
        if (host == null || !GodotObject.IsInstanceValid(host))
        {
            Park();
            return;
        }

        var asset = owner.ReflectionTexture.Target?.Asset;
        // The authored resolution is a CEILING. What renders is what the glass covers on screen, at
        // MirrorDensity texture pixels per screen pixel: a mirror across the room costs a few hundred
        // pixels a side, one you stand in front of gets the full preset, and either way the reflection
        // is as sharp as the eye looking at it. A portal shows the eye's own view through the surface,
        // so it is pinned to the eye's size and only gains the off-screen skip. -xlinka
        int ceiling = System.Math.Clamp(
            owner.LocalResolution > 0 ? owner.LocalResolution
                : owner is Lumora.Core.Components.Mirror mirrorOwner ? mirrorOwner.Resolution.Value
                : asset?.RenderHeight ?? 1024,
            MinEyeHeight, MaxEyeHeight);
        int assetWidthCap = System.Math.Max(MinEyeHeight, (asset?.RenderWidth ?? 2048) / views);
        float renderScale = viewerViewport.Scaling3DScale > 0f && float.IsFinite(viewerViewport.Scaling3DScale)
            ? viewerViewport.Scaling3DScale : 1f;
        Vector2 eyePixels = (stereo ? xr!.GetRenderTargetSize() : viewerSize) * renderScale;
        if (!(eyePixels.X >= 1f) || !(eyePixels.Y >= 1f))
            eyePixels = new Vector2(1024f, 1024f);
        var glassSize = owner.SurfaceSize.Value;
        var glass = new Transform3D(portalXf.Basis, glassPos);
        bool hasGlass = glassSize.x > 0f && glassSize.y > 0f;

        for (int v = 0; v < views; v++)
        {
            Transform3D eye;
            Projection projection;
            if (stereo)
            {
                float requestNear = owner.OverrideNearClip.Value ?? viewer.Near;
                float requestFar = owner.OverrideFarClip.Value ?? viewer.Far;
                eye = xr!.GetTransformForView((uint)v, originXf);
                projection = xr.GetProjectionForView((uint)v, viewerAspect, requestNear, requestFar);
            }
            else
            {
                eye = viewer.GlobalTransform;
                projection = viewer.GetCameraProjection();
            }

            Vector2 edgePixels = eyePixels;
            if (hasGlass && !ProjectSurface(eye, projection, glass, new Vector2(glassSize.x, glassSize.y), eyePixels, out edgePixels))
            {
                // None of the glass is in this eye's view: keep the last picture, skip the render.
                SuspendView(v);
                continue;
            }

            if (surfaceMirror)
            {
                var surface = new Transform3D(portalXf.Basis, planePos);
                float worldWidth = glassSize.x * portalXf.Basis.X.Length();
                float worldHeight = glassSize.y * portalXf.Basis.Y.Length();
                float aspect = worldHeight > 1e-6f ? worldWidth / worldHeight : 1f;
                // The texture's long side follows the surface's long side; ask for enough that both
                // axes reach MirrorDensity over the length they occupy on screen.
                float needX = edgePixels.X * MirrorDensity;
                float needY = edgePixels.Y * MirrorDensity;
                float longSide = aspect >= 1f
                    ? System.MathF.Max(needX, needY * aspect)
                    : System.MathF.Max(needY, needX / aspect);
                int resolution = Settle(v, longSide, ceiling);
                if (!MirrorView.TryCreate(surface, new Vector2(glassSize.x, glassSize.y), eye.Origin, resolution,
                    owner.LocalRenderDistance ?? owner.OverrideFarClip.Value ?? 40f, out var mirrorView))
                {
                    Park();
                    return;
                }
                var mirrorViewport = EnsureViewport(v, host);
                var mirrorCamera = _cameras[v]!;
                mirrorView.Apply(mirrorCamera, mirrorViewport);
                ConfigureCapture(mirrorCamera, mirrorViewport, owner);
                continue;
            }

            // The near-plane rectangle this projection describes, and its centre. For a frustum matrix
            // P, P[0][0] = 2n/(r-l) and P[2][0] = (r+l)/(r-l), so width = 2n/P00 and the centre is
            // P20 * width / 2; the same down the other axis.
            float zn = (float)projection.GetZNear();
            float zf = (float)projection.GetZFar();
            if (zn <= 0f || projection.X.X == 0f || projection.Y.Y == 0f)
                continue;
            float width = 2f * zn / projection.X.X;
            float height = 2f * zn / projection.Y.Y;
            float centreX = projection.Z.X * width * 0.5f;
            float centreY = projection.Z.Y * height * 0.5f;
            float eyeAspect = width / height;

            Transform3D cam;
            if (portal)
            {
                cam = portalMap * eye;
            }
            else
            {
                var pos = ReflectPoint(eye.Origin, planeNormal, planePos);
                var forward = ReflectDirection(-eye.Basis.Z, planeNormal);
                var up = ReflectDirection(eye.Basis.Y, planeNormal);
                cam = new Transform3D(Basis.LookingAt(forward, up), pos);
                centreX = -centreX;
            }

            float near = owner.OverrideNearClip.Value ?? zn;
            float far = owner.OverrideFarClip.Value ?? zf;
            near = System.Math.Max(0.001f, near);
            far = System.Math.Max(near + 0.01f, far);

            // Push the near plane out to the plane's nearest visible depth. The rectangle scales with
            // the near distance to keep the same field of view.
            float planeDepth = NearestPlaneDepth(cam, planeNormal, planePos, width, height, centreX, centreY, zn);
            if (planeDepth > near)
                near = System.Math.Min(planeDepth * NearPlaneFraction, far - 0.01f);
            float scale = near / zn;

            var viewport = EnsureViewport(v, host);
            var camera = _cameras[v]!;

            int eyeHeight = Settle(v, eyePixels.Y * MirrorDensity, ceiling);
            int widthCap = System.Math.Min(assetWidthCap,
                System.Math.Max(MinEyeHeight, (int)System.MathF.Ceiling(eyePixels.X * MirrorDensity)));
            int eyeWidth = (int)System.MathF.Round(eyeHeight * eyeAspect);
            int useHeight = eyeHeight;
            if (eyeWidth > widthCap)
            {
                eyeWidth = widthCap;
                useHeight = System.Math.Max(MinEyeHeight, (int)System.MathF.Round(widthCap / eyeAspect));
            }
            var wanted = new Vector2I(System.Math.Max(1, eyeWidth), System.Math.Max(1, useHeight));
            if (viewport.Size != wanted)
                viewport.Size = wanted;

            // A frustum with no height (the viewer in the plane of the surface, or a surface scaled to
            // nothing) is a projection the renderer cannot intersect; this view sits the frame out.
            float frustumHeight = height * scale;
            if (!float.IsFinite(frustumHeight) || frustumHeight <= 1e-5f
                || !float.IsFinite(centreX * scale) || !float.IsFinite(centreY * scale))
            {
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                continue;
            }
            camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            camera.SetFrustum(frustumHeight, new Vector2(centreX * scale, centreY * scale), near, far);
            camera.GlobalTransform = cam;
            ConfigureCapture(camera, viewport, owner);
        }

        // An eye that is not in use this frame stops rendering rather than showing a stale picture.
        for (int v = views; v < MaxViews; v++)
        {
            var spare = _viewports[v];
            if (spare != null && GodotObject.IsInstanceValid(spare))
                spare.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        }

        _parked = false;
        Publish(views, asset);
    }

    private void ConfigureCapture(Camera3D camera, SubViewport viewport, CameraPortal owner)
    {
        camera.CullMask = unchecked((uint)(RenderHelper.PUBLIC_RENDER_MASK
            & ~RenderHelper.FREECAM_INDICATOR_LAYER & ~RenderHelper.MIRROR_LAYER));
        var viewer = RenderHelper.ViewerCamera?.Invoke();
        var source = owner.OverrideClear.Value ? ClearEnvironment(owner)
            : viewer?.Environment ?? camera.GetWorld3D()?.Environment;
        if (!ReferenceEquals(source, _sourceEnvironment))
        {
            if (_sourceEnvironment != null && GodotObject.IsInstanceValid(_sourceEnvironment))
                _sourceEnvironment.Changed -= EnvironmentChanged;
            _sourceEnvironment = source;
            if (source != null) source.Changed += EnvironmentChanged;
            _environmentDirty = true;
        }
        if (_environmentDirty)
        {
            _captureEnvironment?.Dispose();
            _captureEnvironment = source?.Duplicate() as global::Godot.Environment ?? new global::Godot.Environment();
            // Keep scene lighting in linear HDR. The eye viewport applies the user's tone map and
            // bloom once, after the mirror is composited into the world.
            _captureEnvironment.TonemapMode = global::Godot.Environment.ToneMapper.Linear;
            _captureEnvironment.TonemapExposure = 1f;
            _captureEnvironment.GlowEnabled = false;
            _captureEnvironment.AdjustmentEnabled = false;
            _captureEnvironment.SsrEnabled = false;
            _environmentDirty = false;
        }
        camera.Environment = _captureEnvironment;
        viewport.MeshLodThreshold = EngineSettings.MeshLodThreshold;
        viewport.PositionalShadowAtlasSize = owner.DisableShadows.Value ? 0 : ShadowAtlas;
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    private void EnvironmentChanged() => _environmentDirty = true;

    // The exact nearest depth of the plane over the frustum: the quotient (r.v)/(r.n) is minimised at a
    // corner, so the four corner rays are the whole answer. Zero when the plane is not in front.
    private static float NearestPlaneDepth(in Transform3D cam, Vector3 planeNormal, Vector3 planePos,
        float width, float height, float centreX, float centreY, float zn)
    {
        var camPos = cam.Origin;
        float perpendicular = (planePos - camPos).Dot(planeNormal);
        if (perpendicular <= 0f)
            return 0f;

        var basis = cam.Basis;
        var forward = -basis.Z;
        float best = float.MaxValue;
        for (int corner = 0; corner < 4; corner++)
        {
            float x = centreX + ((corner & 1) == 0 ? -width : width) * 0.5f;
            float y = centreY + ((corner & 2) == 0 ? -height : height) * 0.5f;
            var ray = (basis.X * x + basis.Y * y + forward * zn).Normalized();
            float rn = ray.Dot(planeNormal);
            if (rn <= 1e-5f)
                return 0f;
            float depth = perpendicular / rn * ray.Dot(forward);
            if (depth < best)
                best = depth;
        }
        return best == float.MaxValue ? 0f : best;
    }

    private static Vector3 ReflectPoint(Vector3 point, Vector3 normal, Vector3 planePoint)
        => point - normal * (2f * (point - planePoint).Dot(normal));

    private static Vector3 ReflectDirection(Vector3 direction, Vector3 normal)
        => direction - normal * (2f * direction.Dot(normal));

    private SubViewport EnsureViewport(int index, Node3D host)
    {
        var viewport = _viewports[index];
        if (viewport != null && GodotObject.IsInstanceValid(viewport))
            return viewport;

        viewport = new SubViewport
        {
            Name = index == 0 ? "CameraPortalLeft" : "CameraPortalRight",
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = false,
            HandleInputLocally = false,
            Disable3D = false,
            // A fixed full-detail capture: the reflected camera's changing frustum must not
            // trigger mesh LOD pops or a distance-dependent resolution/AA preset.
            Msaa3D = Viewport.Msaa.Msaa2X,
            MeshLodThreshold = EngineSettings.MeshLodThreshold,
            Scaling3DScale = 1f,
            UseTaa = false,
            PositionalShadowAtlas16Bits = false,
            UseOcclusionCulling = false,
            TransparentBg = false,
            UseHdr2D = true,
        };
        var camera = new Camera3D
        {
            Name = "PortalCamera",
            PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off,
        };
        viewport.AddChild(camera);
        host.AddChild(viewport);
        var world = host.GetWorld3D();
        if (world != null)
            viewport.World3D = world;
        camera.Current = true;

        _viewports[index] = viewport;
        _cameras[index] = camera;
        lock (_liveViewportLock)
            _liveViewports.Add(viewport);
        _publishedViews = -1;
        return viewport;
    }

    // The pictures reach the material through the render texture asset, which every material bound
    // through the ordinary path. Re-notifying it is what rebinds them, and that only has to happen when
    // the set of pictures changes, not every frame. -xlinka
    // The glass as one eye sees it: the longest projected edge along each surface axis, in that eye's
    // pixels, and whether any of it lies inside the view. A corner behind the eye counts as covering
    // the view twice over; that close, the ceiling is the right answer and clipping is not worth the risk.
    private static bool ProjectSurface(in Transform3D eye, in Projection projection, in Transform3D surface,
        Vector2 size, Vector2 viewPixels, out Vector2 edgePixels)
    {
        var toView = eye.AffineInverse();
        Span<Vector2> corners = stackalloc Vector2[4];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -0.5f : 0.5f;
            float sy = (i & 2) == 0 ? -0.5f : 0.5f;
            var corner = surface.Origin + surface.Basis.X * (size.X * sx) + surface.Basis.Y * (size.Y * sy);
            var view = toView * corner;
            var clip = projection * new Vector4(view.X, view.Y, view.Z, 1f);
            if (!(clip.W > 1e-5f))
            {
                edgePixels = viewPixels * 2f;
                return true;
            }
            float nx = clip.X / clip.W;
            float ny = clip.Y / clip.W;
            if (!float.IsFinite(nx) || !float.IsFinite(ny))
            {
                edgePixels = viewPixels * 2f;
                return true;
            }
            corners[i] = new Vector2(nx * 0.5f * viewPixels.X, ny * 0.5f * viewPixels.Y);
            minX = System.MathF.Min(minX, nx);
            maxX = System.MathF.Max(maxX, nx);
            minY = System.MathF.Min(minY, ny);
            maxY = System.MathF.Max(maxY, ny);
        }
        float edgeX = System.MathF.Max(corners[1].DistanceTo(corners[0]), corners[3].DistanceTo(corners[2]));
        float edgeY = System.MathF.Max(corners[2].DistanceTo(corners[0]), corners[3].DistanceTo(corners[1]));
        edgePixels = new Vector2(edgeX, edgeY);
        return maxX >= -1f && minX <= 1f && maxY >= -1f && minY <= 1f;
    }

    // Sizes move in SizeStep pixel steps, grow at once and shrink only once they are clearly smaller,
    // so a viewer swaying in place does not reallocate render targets every frame.
    private int Settle(int view, float needed, int ceiling)
    {
        int quantised = (int)System.MathF.Ceiling(System.Math.Max(1f, needed) / SizeStep) * SizeStep;
        quantised = System.Math.Clamp(quantised, MinEyeHeight, ceiling);
        int current = _settled[view];
        if (current <= 0 || quantised > current || quantised <= current - ShrinkHysteresis || current > ceiling)
            _settled[view] = quantised;
        return _settled[view];
    }

    private void SuspendView(int view)
    {
        var viewport = _viewports[view];
        if (viewport != null && GodotObject.IsInstanceValid(viewport))
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    private void Publish(int views, Lumora.Core.Assets.RenderTexture? asset)
    {
        if (_boundTexture == null || _publishedViews == views)
            return;
        var left = _viewports[0];
        var right = views > 1 ? _viewports[1] : null;
        _boundTexture.SetPortalTextures(
            left != null && GodotObject.IsInstanceValid(left) ? left.GetTexture() : null,
            right != null && GodotObject.IsInstanceValid(right) ? right.GetTexture() : null);
        _publishedViews = views;
        asset?.NotifyBackingReady();
    }

    private global::Godot.Environment ClearEnvironment(CameraPortal owner)
    {
        var env = _clearEnvironment ??= new global::Godot.Environment();
        switch (owner.Clear.Value)
        {
            case ClearMode.Skybox:
                env.BackgroundMode = global::Godot.Environment.BGMode.Sky;
                env.Sky = SkyEnvironment.CurrentSky;
                break;
            case ClearMode.Color:
                env.BackgroundMode = global::Godot.Environment.BGMode.Color;
                var c = owner.ClearColor.Value;
                env.BackgroundColor = new Color(c.r, c.g, c.b, c.a);
                break;
            default:
                env.BackgroundMode = global::Godot.Environment.BGMode.Color;
                env.BackgroundColor = new Color(0f, 0f, 0f, 0f);
                break;
        }
        SkyEnvironment.WritePost(env);
        return env;
    }

    private void Park()
    {
        if (_parked)
            return;
        _parked = true;
        if (_boundTexture != null)
        {
            if (_offTexture == null)
            {
                using var offImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
                offImage.Fill(new Color(0.018f, 0.025f, 0.04f));
                _offTexture = ImageTexture.CreateFromImage(offImage);
            }
            _boundTexture.SetPortalTextures(_offTexture, null);
            _boundAsset?.NotifyBackingReady();
            _publishedViews = -1;
        }
        for (int i = 0; i < MaxViews; i++)
        {
            var viewport = _viewports[i];
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        }
    }

    private void ReleaseTexture()
    {
        if (_boundTexture == null)
            return;
        _boundTexture.SetPortalTextures(null, null);
        _boundAsset?.NotifyBackingReady();
        _boundTexture = null;
        _boundAsset = null;
        _publishedViews = -1;
    }

    private sealed partial class PortalProcessNode : Node3D
    {
        private readonly CameraPortalHook _hook;
        public PortalProcessNode(CameraPortalHook hook) => _hook = hook;
        public PortalProcessNode() => _hook = null!;

        public override void _EnterTree() => RenderingServer.FramePreDraw += UpdateView;
        public override void _ExitTree() => RenderingServer.FramePreDraw -= UpdateView;
        private void UpdateView() => _hook?.Tick();
    }
}
