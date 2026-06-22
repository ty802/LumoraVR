// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Interaction;

[ComponentCategory("XR/Interaction")]
[DefaultUpdateOrder(-900)]
public sealed class InteractionLaser : Component
{
    public readonly Sync<float> MaxDistance = new();
    public readonly Sync<float> BeamRadius = new();
    public readonly Sync<float> BeamStartOffset = new();
    public readonly Sync<Chirality> ControllerSide = new();
    public readonly Sync<colorHDR> IdleColor = new();
    public readonly Sync<colorHDR> HoverColor = new();
    public readonly Sync<float> HoverSwitchTolerance = new();
    public readonly Sync<float> DefaultHoverRadius = new();
    public readonly Sync<float> StickyHitDistance = new();
    public readonly Sync<float> VisualSmoothing = new();
    public readonly Sync<float> CursorSize = new();
    public readonly Sync<bool> ShowDesktopBeam = new();
    public readonly Sync<bool> ShowDirectCursor = new();

    private readonly List<TargetHit> _hitBuffer = new(64);
    // Reused each raycast so the per-frame whole-world collider walk doesn't allocate a fresh list (plus an
    // iterator at every slot) every frame, per laser. -xlinka
    private readonly List<Collider> _colliderBuffer = new(64);
    // Same idea for interaction targets: pulled flat from the world's registry instead of recursing the tree. -xlinka
    private readonly List<IInteractionTarget> _interactionTargetBuffer = new(64);
    private Slot? _beamSlot;
    private Slot? _pointSlot;
    private Slot? _cursorSlot;
    private Slot? _cursorRootSlot;
    private Slot? _directCursorSlot;
    private Slot? _directLineSlot;
    private CurvedBeamMesh? _beamMesh;
    private OverlayUnlitMaterial? _beamMaterial;
    private OverlayUnlitMaterial? _cursorMaterial;
    private OverlayUnlitMaterial? _directCursorMaterial;
    private OverlayUnlitMaterial? _directLineMaterial;
    private SegmentMesh? _directLineMesh;
    private MeshRenderer? _beamRenderer;
    private MeshRenderer? _cursorRenderer;
    private MeshRenderer? _directCursorRenderer;
    private MeshRenderer? _directLineRenderer;
    private Slot? _ignoreRoot;
    private IInteractionTarget? _currentTarget;
    private RayTarget? _currentRayTarget;
    private ILaserPointerTarget? _currentPointerTarget;
    private Slot? _currentHitSlot;
    private float3 _currentHitPoint;
    private float _currentHitDistance;
    private bool _prevSecondaryState;
    private float3 _smoothedHitPoint;
    private bool _hasSmoothedHitPoint;
    private bool _toolPrimaryPressed;
    private bool _toolBlocksPointerActions;
    private float3 _rayOrigin;
    private float3 _rayDirection = float3.Backward;
    private long _lastRefreshFrame = long.MinValue;
    private float _laserVisibleLerp;
    private float3 _visualActualPoint;
    private bool _hasVisualActualPoint;
    private color _currentStartColor = color.Transparent;
    private color _currentEndColor = color.Transparent;
    private float _lastDirectHitDistance;
    private float2 _laserTextureOffset;

    public IInteractionTarget? CurrentTarget => _currentTarget;
    public RayTarget? CurrentRayTarget => _currentRayTarget;
    public ILaserPointerTarget? CurrentPointerTarget => _currentPointerTarget;
    public Slot? CurrentHitSlot => _currentHitSlot;
    public float3 CurrentHitPoint => _currentHitPoint;
    public float CurrentHitDistance => _currentHitDistance;
    public float3 RayOrigin => _rayOrigin;
    public float3 RayDirection => _rayDirection;
    public bool IsActive => _currentTarget != null;

    public event Action<IInteractionTarget?>? TargetChanged;
    public event Action<IInteractionTarget, float3>? Activated;

    private readonly struct TargetHit
    {
        public readonly IInteractionTarget Target;
        public readonly Slot Slot;
        public readonly float Distance;
        public readonly float3 HitPoint;

        public TargetHit(IInteractionTarget target, Slot slot, float distance, float3 hitPoint)
        {
            Target = target;
            Slot = slot;
            Distance = distance;
            HitPoint = hitPoint;
        }
    }

    public override void OnInit()
    {
        base.OnInit();
        MaxDistance.Value = 8f;
        BeamRadius.Value = 0.003f;
        BeamStartOffset.Value = 0.020f;
        ControllerSide.Value = Chirality.Right;
        IdleColor.Value = new colorHDR(0.30f, 0.85f, 1.00f, 0.55f);
        HoverColor.Value = new colorHDR(1.00f, 1.00f, 1.00f, 0.85f);
        HoverSwitchTolerance.Value = 0.05f;
        DefaultHoverRadius.Value = 0.05f;
        StickyHitDistance.Value = 0.08f;
        VisualSmoothing.Value = 12f;
        CursorSize.Value = 0.018f;
        ShowDesktopBeam.Value = false;
        ShowDirectCursor.Value = true;
    }

    public override void OnStart()
    {
        base.OnStart();
        BuildBeamVisual();
        LumoraLogger.Log($"InteractionLaser: Started on '{Slot.SlotName.Value}' side={ControllerSide.Value}");
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        RefreshNow(delta);
    }

    public override void OnDestroy()
    {
        ClearCurrentTarget();
        base.OnDestroy();
    }

    public void SetIgnoreRoot(Slot? root)
    {
        _ignoreRoot = root;
    }

    public void SetToolState(bool primaryPressed, bool blockPointerActions)
    {
        _toolPrimaryPressed = primaryPressed;
        _toolBlocksPointerActions = blockPointerActions;
    }

    // Desktop aim steering: while the context menu owns the mouse the camera is
    // frozen, so the mouse instead deflects the laser ray off head-forward
    // (yaw, pitch in radians). The laser cursor stays the one and only cursor. - xlinka
    private float2 _desktopAimOffset;

    public void SetDesktopAimOffset(in float2 yawPitch)
    {
        _desktopAimOffset = yawPitch;
    }

    // While set, only hits under this slot are interactable and world colliders
    // don't block the ray. Used while the context menu is open: the menu is the
    // only thing the summoning laser can touch. - xlinka
    private Slot? _exclusiveRoot;

    public void SetExclusiveRoot(Slot? root)
    {
        _exclusiveRoot = root;
    }

    // When true the beam mesh never shows, only the cursor. The userspace dash pointer
    // uses this: you want the little cursor on the panel, not a laser line stabbing out
    // of nothing (the userspace pointer has no visible hand to anchor a beam to). -xlinka
    private bool _suppressBeam;

    public void SetBeamSuppressed(bool suppressed)
    {
        _suppressBeam = suppressed;
    }

    // Dormant pointer: skips the cast and pulls all visuals offscreen this frame. The userspace
    // dash pointer flips this with the dash open state, so when the dash is closed there's no
    // stray cursor floating in the overlay world and no wasted raycasts. -xlinka
    private bool _dormant;

    public void SetDormant(bool dormant)
    {
        _dormant = dormant;
    }

    private void HideVisuals()
    {
        _laserVisibleLerp = 0f;
        if (_beamSlot != null) SetIfChanged(_beamSlot.ActiveSelf, false);
        if (_cursorSlot != null) SetIfChanged(_cursorSlot.ActiveSelf, false);
        if (_directCursorSlot != null) SetIfChanged(_directCursorSlot.ActiveSelf, false);
        if (_directLineSlot != null) SetIfChanged(_directLineSlot.ActiveSelf, false);
        if (_beamRenderer != null) SetIfChanged(_beamRenderer.Enabled, false);
        if (_cursorRenderer != null) SetIfChanged(_cursorRenderer.Enabled, false);
        if (_directCursorRenderer != null) SetIfChanged(_directCursorRenderer.Enabled, false);
    }

    public void RefreshNow(float delta)
    {
        long frame = Engine.Current?.FrameCount ?? -1;
        if (frame >= 0 && _lastRefreshFrame == frame)
        {
            return;
        }

        _lastRefreshFrame = frame;
        CastAndUpdate(delta);
    }

    private void BuildBeamVisual()
    {
        _beamSlot = Slot.AddSlot("InteractionLaserVisual");

        _beamMesh = _beamSlot.AttachComponent<CurvedBeamMesh>();
        _beamMesh.Radius.Value = BeamRadius.Value;
        _beamMesh.Sides.Value = 6;
        _beamMesh.Segments.Value = 16;
        _beamMesh.Capped.Value = true;
        _beamMesh.StartPoint.Value = float3.Zero;
        _beamMesh.DirectTargetPoint.Value = float3.Backward * MaxDistance.Value;
        _beamMesh.ActualTargetPoint.Value = float3.Backward * MaxDistance.Value;
        _beamMesh.StartPointColor.Value = IdleColor.Value.ToLDR();
        _beamMesh.EndPointColor.Value = IdleColor.Value.ToLDR();

        var matSlot = _beamSlot.AddSlot("InteractionLaserMaterial");
        _beamMaterial = matSlot.AttachComponent<OverlayUnlitMaterial>();
        _beamMaterial.FrontTintColor.Value = colorHDR.White;
        _beamMaterial.BehindTintColor.Value = new colorHDR(0.5f, 0.5f, 0.5f, 0.35f);
        _beamMaterial.UseVertexColor.Value = true;
        _beamMaterial.BlendMode.Value = BlendMode.Additive;
        _beamMaterial.Culling.Value = Culling.None;
        _beamMaterial.RenderQueue.Value = 4010;

        _beamRenderer = _beamSlot.AttachComponent<MeshRenderer>();
        _beamRenderer.Mesh.Target = _beamMesh;
        _beamRenderer.Material.Target = _beamMaterial;
        _beamRenderer.ShadowCastMode.Value = ShadowCastMode.Off;
        _beamRenderer.SortingOrder.Value = 110;

        _pointSlot = _beamSlot.AddSlot("Point");
        _pointSlot.LocalPosition.Value = float3.Backward * MaxDistance.Value;

        // LOCAL slot: the desktop pointer cursor is a per-viewer visual (like an OS mouse cursor), NOT something
        // other users should see. AddLocalSlot mints a LOCAL_BYTE RefID that the world never replicates, so the
        // whole cursor subtree (Image quad, mesh, material - all created under it) stays on this machine only.
        // The beam itself stays replicated above, so a remote user's LASER is still visible during interaction,
        // just not their cursor. Hit-testing/dash clicks use the ray pose, never this slot, so they're unaffected.
        // -xlinka
        _cursorSlot = _pointSlot.AddLocalSlot("Cursor");
        _cursorMaterial = _cursorSlot.AttachComponent<OverlayUnlitMaterial>();
        _cursorMaterial.BlendMode.Value = BlendMode.Additive;
        _cursorMaterial.FrontTintColor.Value = colorHDR.White;
        _cursorMaterial.BehindTintColor.Value = new colorHDR(1f, 1f, 1f, 0.35f);
        _cursorMaterial.UseVertexColor.Value = true;
        _cursorMaterial.RenderQueue.Value = 4005;

        _cursorRootSlot = _cursorSlot.AddSlot("Image");
        var cursorMesh = _cursorRootSlot.AttachComponent<QuadMesh>();
        cursorMesh.Size.Value = float2.One * CursorSize.Value;
        cursorMesh.DualSided.Value = true;
        cursorMesh.Color = color.White;

        _cursorRenderer = _cursorRootSlot.AttachComponent<MeshRenderer>();
        _cursorRenderer.Mesh.Target = cursorMesh;
        _cursorRenderer.Material.Target = _cursorMaterial;
        _cursorRenderer.ShadowCastMode.Value = ShadowCastMode.Off;
        _cursorRenderer.SortingOrder.Value = 105;

        // LOCAL: direct-touch cursor visual, per-viewer only (see the Cursor note above). -xlinka
        _directCursorSlot = _beamSlot.AddLocalSlot("DirectCursor");
        _directCursorMaterial = _directCursorSlot.AttachComponent<OverlayUnlitMaterial>();
        _directCursorMaterial.BlendMode.Value = BlendMode.Additive;
        _directCursorMaterial.FrontTintColor.Value = new colorHDR(1f, 1f, 1f, 0.25f);
        _directCursorMaterial.BehindTintColor.Value = new colorHDR(1f, 1f, 1f, 0.15f);
        _directCursorMaterial.UseVertexColor.Value = true;
        _directCursorMaterial.RenderQueue.Value = 4000;

        var directCursorMesh = _directCursorSlot.AttachComponent<QuadMesh>();
        directCursorMesh.Size.Value = float2.One * (CursorSize.Value * 0.75f);
        directCursorMesh.DualSided.Value = true;
        directCursorMesh.Color = new color(1f, 1f, 1f, 0.25f);

        _directCursorRenderer = _directCursorSlot.AttachComponent<MeshRenderer>();
        _directCursorRenderer.Mesh.Target = directCursorMesh;
        _directCursorRenderer.Material.Target = _directCursorMaterial;
        _directCursorRenderer.ShadowCastMode.Value = ShadowCastMode.Off;
        _directCursorRenderer.SortingOrder.Value = 100;

        // LOCAL: direct-touch line visual, per-viewer only (see the Cursor note above). -xlinka
        _directLineSlot = _beamSlot.AddLocalSlot("DirectLine");
        _directLineMesh = _directLineSlot.AttachComponent<SegmentMesh>();
        _directLineMesh.Radius.Value = BeamRadius.Value * 0.75f;
        _directLineMesh.Sides.Value = 6;
        _directLineMesh.PointA.Value = float3.Backward * MaxDistance.Value;
        _directLineMesh.PointB.Value = float3.Backward * MaxDistance.Value;
        _directLineMesh.PointAColor.Value = new color(1f, 1f, 1f, 0.18f);
        _directLineMesh.PointBColor.Value = new color(1f, 1f, 1f, 0.18f);

        _directLineMaterial = _directLineSlot.AttachComponent<OverlayUnlitMaterial>();
        _directLineMaterial.BlendMode.Value = BlendMode.Additive;
        _directLineMaterial.FrontTintColor.Value = colorHDR.White;
        _directLineMaterial.BehindTintColor.Value = new colorHDR(1f, 1f, 1f, 0.35f);
        _directLineMaterial.UseVertexColor.Value = true;
        _directLineMaterial.RenderQueue.Value = 4000;

        _directLineRenderer = _directLineSlot.AttachComponent<MeshRenderer>();
        _directLineRenderer.Mesh.Target = _directLineMesh;
        _directLineRenderer.Material.Target = _directLineMaterial;
        _directLineRenderer.ShadowCastMode.Value = ShadowCastMode.Off;
        _directLineRenderer.SortingOrder.Value = 100;

        _beamSlot.ActiveSelf.Value = false;
        _cursorSlot.ActiveSelf.Value = false;
        _directCursorSlot.ActiveSelf.Value = false;
        _directLineSlot.ActiveSelf.Value = false;
    }

    private void CastAndUpdate(float delta)
    {
        if (_beamSlot == null || World?.RootSlot == null) return;

        if (_dormant)
        {
            // Pointer is parked (e.g. userspace dash pointer while the dash is closed). Drop the
            // hover target so nothing stays "pressed", hide everything, and skip the cast. -xlinka
            if (_currentTarget != null) UpdatePointerTarget(null, _rayOrigin, _rayDirection, false);
            _currentTarget = null;
            _currentHitSlot = null;
            HideVisuals();
            return;
        }

        float maxDist = MathF.Max(MaxDistance.Value, 0.01f);
        ResolveRayPose(out float3 origin, out float3 direction);
        _rayOrigin = origin;
        _rayDirection = direction;

        bool exclusive = _exclusiveRoot != null && !_exclusiveRoot.IsDestroyed;

        float blockingDistance = maxDist;
        if (!exclusive && TryFindNearestColliderHitDistance(origin, direction, maxDist, out float colliderHitDistance))
        {
            blockingDistance = colliderHitDistance;
        }

        _hitBuffer.Clear();
        CollectInteractionHits(World, origin, direction, maxDist, blockingDistance);

        // The dashboard lives in the userspace overlay world and is pointed at by a rig that also
        // lives there (the userspace pointer rig, see UserspacePointer) in BOTH desktop and VR. So
        // this in-world laser only ever touches its OWN world - no cross-world cast. That's what
        // lets the dash keep a working cursor even after you delete the world you were standing in:
        // the pointer was never tied to this avatar to begin with. -xlinka

        if (exclusive)
        {
            for (int i = _hitBuffer.Count - 1; i >= 0; i--)
            {
                var hitSlot = _hitBuffer[i].Slot;
                if (hitSlot == null || (hitSlot != _exclusiveRoot && !hitSlot.IsDescendantOf(_exclusiveRoot!)))
                {
                    _hitBuffer.RemoveAt(i);
                }
            }
        }

        IInteractionTarget? hoveredTarget = null;
        Slot? hoveredSlot = null;
        float hoveredDistance = maxDist;
        float3 hoveredHitPoint = float3.Zero;

        if (_hitBuffer.Count > 0)
        {
            int nearestIndex = 0;
            float nearestDistance = _hitBuffer[0].Distance;
            for (int i = 1; i < _hitBuffer.Count; i++)
            {
                if (_hitBuffer[i].Distance < nearestDistance)
                {
                    nearestDistance = _hitBuffer[i].Distance;
                    nearestIndex = i;
                }
            }

            int selectedIndex = nearestIndex;
            float keepThreshold = nearestDistance + MathF.Max(HoverSwitchTolerance.Value, 0f);
            if (_currentTarget != null)
            {
                for (int i = 0; i < _hitBuffer.Count; i++)
                {
                    var hit = _hitBuffer[i];
                    if (ReferenceEquals(hit.Target, _currentTarget) && hit.Distance <= keepThreshold)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            var selected = _hitBuffer[selectedIndex];
            // walk parent chain on the selected slot for a higher-priority target. - xlinka
            var promoted = PromoteToHighestPriority(selected.Target, selected.Slot);
            hoveredTarget = promoted;
            hoveredSlot = selected.Slot;
            hoveredDistance = selected.Distance;
            hoveredHitPoint = selected.HitPoint;
        }
        else if (!exclusive && TryKeepStickyTarget(origin, direction, maxDist, blockingDistance,
            out var stickyTarget, out var stickySlot, out float stickyDistance, out float3 stickyPoint))
        {
            hoveredTarget = stickyTarget;
            hoveredSlot = stickySlot;
            hoveredDistance = stickyDistance;
            hoveredHitPoint = stickyPoint;
        }

        if (!ReferenceEquals(hoveredTarget, _currentTarget))
        {
            ClearCurrentTarget();
            _currentTarget = hoveredTarget;
            _currentRayTarget = hoveredTarget as RayTarget;
            _currentRayTarget?.NotifyHoverEntered();
            TargetChanged?.Invoke(_currentTarget);
        }
        if (hoveredTarget != null)
        {
            hoveredHitPoint = ApplyHitPointSmoothing(hoveredSlot, hoveredHitPoint, delta);
        }
        else
        {
            _hasSmoothedHitPoint = false;
        }
        _currentHitSlot = hoveredSlot;
        _currentHitPoint = hoveredHitPoint;
        _currentHitDistance = hoveredDistance;

        UpdatePointerTarget(hoveredTarget as ILaserPointerTarget, origin, direction, _toolPrimaryPressed);
        ProcessPointerActions();

        float beamLength = hoveredTarget != null ? hoveredDistance : blockingDistance;
        float3 beamEndPoint = hoveredTarget != null
            ? _currentHitPoint
            : new float3(
                origin.x + direction.x * beamLength,
                origin.y + direction.y * beamLength,
                origin.z + direction.z * beamLength);
        PositionBeam(origin, direction, beamLength, beamEndPoint, delta);

        if (_beamMesh != null)
        {
            color wantedColor = (hoveredTarget != null ? HoverColor.Value : IdleColor.Value).ToLDR();
            SetIfChanged(_beamMesh.Radius, BeamRadius.Value);
            UpdateLaserVisual(delta, hoveredTarget != null, wantedColor, beamLength);
        }
    }

    private static void SetIfChanged(Sync<colorHDR> field, colorHDR value)
    {
        colorHDR current = field.Value;
        if (current.r != value.r || current.g != value.g ||
            current.b != value.b || current.a != value.a)
        {
            field.Value = value;
        }
    }

    private static void SetIfChanged(Sync<color> field, color value)
    {
        color current = field.Value;
        if (current.r != value.r || current.g != value.g ||
            current.b != value.b || current.a != value.a)
        {
            field.Value = value;
        }
    }

    private static void SetIfChanged(Sync<float3> field, float3 value)
    {
        float3 current = field.Value;
        if (MathF.Abs(current.x - value.x) > 0.0001f ||
            MathF.Abs(current.y - value.y) > 0.0001f ||
            MathF.Abs(current.z - value.z) > 0.0001f)
        {
            field.Value = value;
        }
    }

    private static void SetIfChanged(Sync<float2> field, float2 value)
    {
        float2 current = field.Value;
        if (MathF.Abs(current.x - value.x) > 0.0001f ||
            MathF.Abs(current.y - value.y) > 0.0001f)
        {
            field.Value = value;
        }
    }

    private static void SetIfChanged(Sync<float> field, float value)
    {
        if (MathF.Abs(field.Value - value) > 0.0001f)
        {
            field.Value = value;
        }
    }

    private static void SetIfChanged(Sync<bool> field, bool value)
    {
        if (field.Value != value)
        {
            field.Value = value;
        }
    }

    private void UpdateLaserVisual(float delta, bool hasTarget, color targetColor, float pointDistance)
    {
        var input = Engine.Current?.InputInterface;
        bool isVr = input?.IsVRActive == true;
        // Desktop has no screen-space reticle - this in-world cursor is the only
        // pointer, so it stays visible even with nothing hovered.
        bool isDesktop = input != null && !isVr;
        bool shouldShow = hasTarget || isVr || isDesktop;
        float visualStep = MathF.Max(delta, 0f) * 6f;
        _laserVisibleLerp = Progress01(_laserVisibleLerp, visualStep, shouldShow);

        bool rootVisible = _laserVisibleLerp > 0.001f;
        bool beamVisible = rootVisible && (isVr || ShowDesktopBeam.Value) && !_suppressBeam;
        bool cursorVisible = rootVisible;
        bool directVisible = rootVisible && ShowDirectCursor.Value && isVr;

        if (_beamSlot != null) SetIfChanged(_beamSlot.ActiveSelf, rootVisible);
        if (_cursorSlot != null) SetIfChanged(_cursorSlot.ActiveSelf, cursorVisible);
        if (_directCursorSlot != null) SetIfChanged(_directCursorSlot.ActiveSelf, directVisible);
        if (_directLineSlot != null) SetIfChanged(_directLineSlot.ActiveSelf, directVisible);
        if (_beamRenderer != null) SetIfChanged(_beamRenderer.Enabled, beamVisible);
        if (_cursorRenderer != null) SetIfChanged(_cursorRenderer.Enabled, cursorVisible);
        if (_directCursorRenderer != null) SetIfChanged(_directCursorRenderer.Enabled, directVisible);
        if (_directLineRenderer != null) SetIfChanged(_directLineRenderer.Enabled, directVisible);

        if (!rootVisible)
        {
            return;
        }

        float endVisibility = MathF.Min(1f, _laserVisibleLerp * 2f);
        float startVisibility = MathF.Min(1f, MathF.Max(0f, _laserVisibleLerp - 0.35f) / 0.65f);
        float colorStep = System.Math.Clamp(MathF.Max(delta, 0f) * 10f, 0f, 1f);
        _currentStartColor = color.Lerp(_currentStartColor, _currentEndColor, colorStep);
        _currentEndColor = color.Lerp(_currentEndColor, targetColor, colorStep);

        color startColor = color.Lerp(_currentStartColor, color.White, 0.25f) * startVisibility;
        color endColor = color.Lerp(_currentEndColor, color.White, 0.25f) * endVisibility;
        SetIfChanged(_beamMesh!.StartPointColor, startColor);
        SetIfChanged(_beamMesh.EndPointColor, endColor);

        color cursorColor = color.Lerp(_currentEndColor, color.White, 0.75f);
        cursorColor.a *= endVisibility;
        if (_cursorMaterial != null)
        {
            var tint = new colorHDR(cursorColor.r, cursorColor.g, cursorColor.b, cursorColor.a);
            SetIfChanged(_cursorMaterial.FrontTintColor, tint);
            SetIfChanged(_cursorMaterial.BehindTintColor, new colorHDR(cursorColor.r, cursorColor.g, cursorColor.b, cursorColor.a * 0.5f));
        }

        if (_directCursorMaterial != null)
        {
            var directTint = new colorHDR(cursorColor.r, cursorColor.g, cursorColor.b, cursorColor.a * 0.25f);
            SetIfChanged(_directCursorMaterial.FrontTintColor, directTint);
            SetIfChanged(_directCursorMaterial.BehindTintColor, new colorHDR(cursorColor.r, cursorColor.g, cursorColor.b, cursorColor.a * 0.15f));
        }

        if (_directLineMaterial != null)
        {
            var lineTint = new colorHDR(cursorColor.r, cursorColor.g, cursorColor.b, cursorColor.a * 0.25f);
            SetIfChanged(_directLineMaterial.FrontTintColor, lineTint);
            SetIfChanged(_directLineMaterial.BehindTintColor, new colorHDR(cursorColor.r, cursorColor.g, cursorColor.b, cursorColor.a * 0.15f));
        }

        OrientCursor(pointDistance);
        _laserTextureOffset += new float2(0f, delta * 2f);
        if (_beamMaterial != null)
        {
            SetIfChanged(_beamMaterial.FrontTextureOffset, _laserTextureOffset);
            SetIfChanged(_beamMaterial.BehindTextureOffset, _laserTextureOffset);
        }
    }

    private static float Progress01(float current, float amount, bool target)
    {
        if (target)
        {
            return System.Math.Clamp(current + amount, 0f, 1f);
        }
        return System.Math.Clamp(current - amount, 0f, 1f);
    }

    private void OrientCursor(float pointDistance)
    {
        if (_pointSlot == null || _cursorSlot == null)
        {
            return;
        }

        var input = Engine.Current?.InputInterface;
        if (input != null && !input.IsVRActive && input.DesktopCameraPoseValid)
        {
            // Desktop: screen-aligned, like an OS cursor. A look-at billboard
            // would visibly yaw and roll as the cursor sweeps across the view.
            _cursorSlot.GlobalRotation = input.DesktopCameraRotation;
        }
        else
        {
            var head = FindHeadSlot();
            float3 viewPosition = head?.GlobalPosition ?? (_rayOrigin - _rayDirection);
            float3 viewUp = head?.Up ?? float3.Up;
            float3 toView = viewPosition - _pointSlot.GlobalPosition;
            if (toView.Length <= 0.0001f)
            {
                toView = -_rayDirection;
            }
            if (viewUp.Length <= 0.0001f)
            {
                viewUp = float3.Up;
            }

            _cursorSlot.GlobalRotation = SafeLookRotation(toView, viewUp);
        }
        float cursorScale = MathF.Max(0.35f, pointDistance) * 0.6f;
        _cursorSlot.LocalScale.Value = float3.One * cursorScale;

        if (_directCursorSlot != null)
        {
            _directCursorSlot.GlobalRotation = _cursorSlot.GlobalRotation;
            _directCursorSlot.LocalScale.Value = float3.One * cursorScale;
        }
    }

    private static floatQ SafeLookRotation(float3 forward, float3 up)
    {
        if (forward.Length <= 0.0001f)
        {
            return floatQ.Identity;
        }

        forward = forward.Normalized;
        if (up.Length <= 0.0001f)
        {
            up = float3.Up;
        }

        up = up - forward * float3.Dot(up, forward);
        if (up.Length <= 0.0001f)
        {
            up = MathF.Abs(float3.Dot(forward, float3.Up)) > 0.95f ? float3.Right : float3.Up;
            up = up - forward * float3.Dot(up, forward);
        }

        return floatQ.LookRotation(forward, up.Normalized);
    }

    private void ClearCurrentTarget()
    {
        if (_currentRayTarget != null)
        {
            _currentRayTarget.NotifyHoverExited();
            _currentRayTarget = null;
        }
        _currentTarget = null;
        _currentHitSlot = null;
        ClearPointerTarget();
        _hasSmoothedHitPoint = false;
    }

    private void UpdatePointerTarget(ILaserPointerTarget? pointerTarget, float3 origin, float3 direction, bool isPressed)
    {
        int pointerId = GetPointerId();
        if (!ReferenceEquals(pointerTarget, _currentPointerTarget))
        {
            _currentPointerTarget?.ClearLaserPointer(this, pointerId);
            _currentPointerTarget = pointerTarget;
        }

        _currentPointerTarget?.UpdateLaserPointer(this, pointerId, origin, direction, isPressed);
    }

    private void ProcessPointerActions()
    {
        int pointerId = GetPointerId();

        if (!_toolBlocksPointerActions && _currentPointerTarget is ILaserAxisTarget axisTarget)
        {
            float2 axis = ReadPointerAxis();
            if (axis != float2.Zero)
            {
                axisTarget.ProcessLaserAxis(this, pointerId, in axis);
            }
        }

        bool secondaryNow = ReadSecondaryPressed();
        if (!_toolBlocksPointerActions && secondaryNow && !_prevSecondaryState &&
            _currentPointerTarget is ILaserSecondaryTarget secondaryTarget)
        {
            secondaryTarget.TriggerLaserSecondary(this, pointerId);
        }
        _prevSecondaryState = secondaryNow;
    }

    private void ClearPointerTarget()
    {
        if (_currentPointerTarget == null) return;

        _currentPointerTarget.ClearLaserPointer(this, GetPointerId());
        _currentPointerTarget = null;
    }

    private int GetPointerId()
    {
        return ControllerSide.Value == Chirality.Left ? 1 : 2;
    }

    internal void NotifyActivatedByTool(IInteractionTarget target, float3 point)
    {
        Activated?.Invoke(target, point);
    }

    // walk parent chain from the hit slot. take the highest-priority IInteractionTarget
    // found. stop at SearchBlock on a non-origin ancestor. - xlinka
    private static IInteractionTarget PromoteToHighestPriority(IInteractionTarget seed, Slot hitSlot)
    {
        IInteractionTarget best = seed;
        int bestPriority = seed.InteractionTargetPriority;
        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }
            foreach (var t in current.GetComponentsImplementing<IInteractionTarget>())
            {
                if (t.InteractionTargetPriority > bestPriority)
                {
                    best = t;
                    bestPriority = t.InteractionTargetPriority;
                }
            }
            current = current.Parent;
        }
        return best;
    }

    private bool TryKeepStickyTarget(float3 origin, float3 direction, float maxDistance, float blockingDistance,
        out IInteractionTarget? target, out Slot? slot, out float distance, out float3 hitPoint)
    {
        target = null;
        slot = null;
        distance = 0f;
        hitPoint = float3.Zero;

        if (_currentTarget == null || _currentHitSlot == null) return false;
        if (_currentTarget is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) return false;
        if (!_currentHitSlot.IsActive) return false;

        float3 oldDelta = new(
            _currentHitPoint.x - origin.x,
            _currentHitPoint.y - origin.y,
            _currentHitPoint.z - origin.z);
        float projected = float3.Dot(oldDelta, direction);
        if (projected <= 0f || projected > maxDistance || projected > blockingDistance) return false;

        float3 candidate = new(
            origin.x + direction.x * projected,
            origin.y + direction.y * projected,
            origin.z + direction.z * projected);
        float3 miss = new(
            candidate.x - _currentHitPoint.x,
            candidate.y - _currentHitPoint.y,
            candidate.z - _currentHitPoint.z);
        if (miss.Length > MathF.Max(StickyHitDistance.Value, 0f)) return false;
        if (!TryApplyLaserModifiers(_currentHitSlot, direction, candidate, out candidate)) return false;

        target = _currentTarget;
        slot = _currentHitSlot;
        distance = projected;
        hitPoint = candidate;
        return true;
    }

    private bool TryApplyLaserModifiers(Slot hitSlot, float3 direction, float3 point, out float3 filteredPoint)
    {
        filteredPoint = point;

        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }

            foreach (var modifier in current.GetComponentsImplementing<ILaserInteractionModifier>())
            {
                if (modifier is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) continue;
                if (!modifier.IsInteractionHit(filteredPoint, direction)) return false;
                filteredPoint = modifier.FilterPoint(this, filteredPoint);
            }

            current = current.Parent;
        }

        return true;
    }

    private float3 ApplyHitPointSmoothing(Slot? hitSlot, float3 newPoint, float delta)
    {
        if (hitSlot == null)
        {
            _smoothedHitPoint = newPoint;
            _hasSmoothedHitPoint = true;
            return newPoint;
        }

        if (!_hasSmoothedHitPoint)
        {
            _smoothedHitPoint = newPoint;
            _hasSmoothedHitPoint = true;
            return newPoint;
        }

        float? smoothSpeed = null;
        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }

            foreach (var modifier in current.GetComponentsImplementing<ILaserInteractionModifier>())
            {
                if (modifier is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) continue;
                float? speed = modifier.GetSmoothSpeed(this, newPoint, _smoothedHitPoint);
                if (!speed.HasValue) continue;
                smoothSpeed = smoothSpeed.HasValue
                    ? MathF.Min(smoothSpeed.Value, speed.Value)
                    : speed.Value;
            }

            current = current.Parent;
        }

        if (!smoothSpeed.HasValue || smoothSpeed.Value <= 0f)
        {
            _smoothedHitPoint = newPoint;
            return newPoint;
        }

        float t = 1f - MathF.Exp(-smoothSpeed.Value * MathF.Max(delta, 0f));
        _smoothedHitPoint = float3.Lerp(_smoothedHitPoint, newPoint, System.Math.Clamp(t, 0f, 1f));
        return _smoothedHitPoint;
    }

    // Pull every interaction target in this world straight from its registry instead of recursing the whole
    // slot tree per frame, per laser. Each candidate is fully filtered below (enabled/active/hierarchy), so a
    // momentarily stale registry entry can't produce a wrong hit - same guarantee as the collider raycast.
    // The old subtree skip (!slot.IsActive -> return) becomes a per-target slot.IsActive check, which is the
    // hierarchical active state, so an inactive ancestor still excludes its targets. -xlinka
    private void CollectInteractionHits(World? world, float3 origin, float3 direction, float maxDist, float blockingDistance)
    {
        if (world == null) return;

        world.CopyInteractionTargetsTo(_interactionTargetBuffer);
        for (int ti = 0; ti < _interactionTargetBuffer.Count; ti++)
        {
            var target = _interactionTargetBuffer[ti];

            // Registry only holds Components (registration is in ComponentBase), so the slot is the component's
            // own slot - the same slot the old recursion was visiting it on.
            if (target is not Component comp) continue;
            if (!comp.Enabled.Value || comp.IsDestroyed) continue;
            var slot = comp.Slot;
            if (slot == null || !slot.IsActive) continue;

            if (target is ILaserPointerTarget pointerTarget)
            {
                if (!pointerTarget.TryGetLaserPointerHit(this, origin, direction, maxDist, out var pointerHit)) continue;
                if (pointerHit.Distance > blockingDistance) continue;
                if (IsSlotOnThisHierarchy(slot)) continue;

                _hitBuffer.Add(new TargetHit(target, slot, pointerHit.Distance, pointerHit.Point));
                continue;
            }

            float radius = GetHoverRadius(target);
            if (radius <= 0f) continue;

            float3 center = slot.GlobalPosition;
            if (!RaySphereIntersect(origin, direction, center, radius, out float distance)) continue;
            if (distance > maxDist || distance > blockingDistance) continue;
            if (IsSlotOnThisHierarchy(slot)) continue;

            var hitPoint = new float3(
                origin.x + direction.x * distance,
                origin.y + direction.y * distance,
                origin.z + direction.z * distance);
            if (!TryApplyLaserModifiers(slot, direction, hitPoint, out hitPoint)) continue;

            _hitBuffer.Add(new TargetHit(target, slot, distance, hitPoint));
        }
    }

    private float GetHoverRadius(IInteractionTarget target)
    {
        if (target is RayTarget rt) return MathF.Max(rt.HoverRadius.Value, 0.001f);
        return MathF.Max(DefaultHoverRadius.Value, 0.001f);
    }

    private void ResolveRayPose(out float3 origin, out float3 direction)
    {
        float startOffset = MathF.Max(BeamStartOffset.Value, 0f);
        var input = Engine.Current?.InputInterface;

        // desktop: aim from the head/camera so the ray passes through the reticle. - xlinka
        if (input != null && !input.IsVRActive)
        {
            // Free-cursor mode (dash open): the platform-supplied ray through the
            // unlocked OS cursor IS the laser ray, so the in-world cursor follows
            // the mouse over the projected dash.
            if (input.IsDashboardOpen && input.DesktopCursorRayValid)
            {
                direction = input.DesktopCursorRayDirection;
                float cursorLen = direction.Length;
                direction = cursorLen > 0.0001f ? direction / cursorLen : float3.Backward;
                origin = input.DesktopCursorRayOrigin + direction * startOffset;
                return;
            }

            var headSlot = FindHeadSlot();
            floatQ headRot = headSlot != null ? headSlot.GlobalRotation : floatQ.Identity;
            float3 headPos = headSlot != null ? headSlot.GlobalPosition : Slot.GlobalPosition;

            // Mouse-steered deflection while the camera is frozen (context menu open).
            if (_desktopAimOffset != float2.Zero)
            {
                headRot = headRot
                    * floatQ.AxisAngle(float3.Up, _desktopAimOffset.x)
                    * floatQ.AxisAngle(new float3(1f, 0f, 0f), _desktopAimOffset.y);
            }

            direction = headRot * new float3(0f, 0f, -1f);
            float dLen = direction.Length;
            if (dLen > 0.0001f) direction /= dLen;
            else direction = float3.Backward;

            origin = new float3(
                headPos.x + direction.x * startOffset,
                headPos.y + direction.y * startOffset,
                headPos.z + direction.z * startOffset);
            return;
        }

        // VR: controller slot forward, with optional fingertip refinement. - xlinka
        direction = -Slot.Forward;
        float dirLen = direction.Length;
        if (dirLen < 0.0001f) direction = float3.Backward;
        else direction /= dirLen;

        origin = new float3(
            Slot.GlobalPosition.x + direction.x * startOffset,
            Slot.GlobalPosition.y + direction.y * startOffset,
            Slot.GlobalPosition.z + direction.z * startOffset);

        if (input == null) return;

        BodyNode tipNode = ControllerSide.Value == Chirality.Left
            ? BodyNode.LeftIndexFinger_Tip
            : BodyNode.RightIndexFinger_Tip;
        BodyNode distalNode = ControllerSide.Value == Chirality.Left
            ? BodyNode.LeftIndexFinger_Distal
            : BodyNode.RightIndexFinger_Distal;

        var tip = input.GetBodyNode(tipNode);
        if (tip == null || !tip.IsTracking) return;

        float3 resolvedOrigin = tip.Position;
        var distal = input.GetBodyNode(distalNode);
        if (distal != null && distal.IsTracking)
        {
            float3 fingertipDirection = new float3(
                tip.Position.x - distal.Position.x,
                tip.Position.y - distal.Position.y,
                tip.Position.z - distal.Position.z);
            float ftLen = fingertipDirection.Length;
            if (ftLen > 0.0001f) direction = fingertipDirection / ftLen;
        }

        origin = new float3(
            resolvedOrigin.x + direction.x * startOffset,
            resolvedOrigin.y + direction.y * startOffset,
            resolvedOrigin.z + direction.z * startOffset);
    }

    private bool TryFindNearestColliderHitDistance(float3 origin, float3 direction, float maxDistance, out float hitDistance)
    {
        hitDistance = maxDistance;
        bool hasHit = false;

        // Pull from the world's collider registry instead of walking the whole slot tree every frame. Each
        // candidate is still filtered below, so a stale entry can't cause a wrong hit. -xlinka
        World.CopyCollidersTo(_colliderBuffer);
        foreach (var collider in _colliderBuffer)
        {
            if (!IsColliderRaycastCandidate(collider)) continue;
            if (!TryIntersectCollider(collider, origin, direction, maxDistance, out float candidateDistance)) continue;
            if (candidateDistance < hitDistance)
            {
                hitDistance = candidateDistance;
                hasHit = true;
            }
        }
        return hasHit;
    }

    private bool IsColliderRaycastCandidate(Collider collider)
    {
        if (collider == null || collider.Slot == null) return false;
        if (!collider.Enabled.Value || !collider.Slot.IsActive) return false;
        if (collider.IgnoreRaycasts.Value || collider.Type.Value == ColliderType.NoCollision) return false;
        if (IsSlotOnThisHierarchy(collider.Slot)) return false;
        return true;
    }

    private static bool TryIntersectCollider(Collider collider, float3 origin, float3 direction, float maxDistance, out float hitDistance)
    {
        hitDistance = 0f;
        if (!TryGetColliderLocalBounds(collider, out float3 localMin, out float3 localMax)) return false;

        float3 localOrigin = collider.Slot.GlobalPointToLocal(origin);
        float3 localDirection = collider.Slot.GlobalDirectionToLocal(direction);
        if (localDirection.Length < 0.0001f) return false;
        if (!RayAabbIntersect(localOrigin, localDirection, localMin, localMax, out float localHitT)) return false;

        float3 localHitPoint = new float3(
            localOrigin.x + localDirection.x * localHitT,
            localOrigin.y + localDirection.y * localHitT,
            localOrigin.z + localDirection.z * localHitT);
        float3 worldHitPoint = collider.Slot.LocalPointToGlobal(localHitPoint);

        float3 worldDelta = new float3(
            worldHitPoint.x - origin.x,
            worldHitPoint.y - origin.y,
            worldHitPoint.z - origin.z);
        float forwardDistance = float3.Dot(worldDelta, direction);
        if (forwardDistance <= 0f) return false;

        float distance = worldDelta.Length;
        if (distance <= 0.0001f || distance > maxDistance) return false;

        hitDistance = distance;
        return true;
    }

    private static bool TryGetColliderLocalBounds(Collider collider, out float3 min, out float3 max)
    {
        switch (collider)
        {
            case BoxCollider box:
            {
                float3 half = AbsVector(box.Size.Value) * 0.5f;
                float3 offset = box.Offset.Value;
                min = new float3(offset.x - half.x, offset.y - half.y, offset.z - half.z);
                max = new float3(offset.x + half.x, offset.y + half.y, offset.z + half.z);
                return true;
            }
            case SphereCollider sphere:
            {
                float radius = MathF.Max(MathF.Abs(sphere.Radius.Value), 0.0005f);
                float3 offset = sphere.Offset.Value;
                min = new float3(offset.x - radius, offset.y - radius, offset.z - radius);
                max = new float3(offset.x + radius, offset.y + radius, offset.z + radius);
                return true;
            }
            case CapsuleCollider capsule:
            {
                float radius = MathF.Max(MathF.Abs(capsule.Radius.Value), 0.0005f);
                float halfHeight = MathF.Max(MathF.Abs(capsule.Height.Value) * 0.5f, radius);
                float3 offset = capsule.Offset.Value;
                min = new float3(offset.x - radius, offset.y - halfHeight, offset.z - radius);
                max = new float3(offset.x + radius, offset.y + halfHeight, offset.z + radius);
                return true;
            }
            case CylinderCollider cylinder:
            {
                float radius = MathF.Max(MathF.Abs(cylinder.Radius.Value), 0.0005f);
                float halfHeight = MathF.Max(MathF.Abs(cylinder.Height.Value) * 0.5f, 0.0005f);
                float3 offset = cylinder.Offset.Value;
                min = new float3(offset.x - radius, offset.y - halfHeight, offset.z - radius);
                max = new float3(offset.x + radius, offset.y + halfHeight, offset.z + radius);
                return true;
            }
            default:
                min = float3.Zero;
                max = float3.Zero;
                return false;
        }
    }

    private static bool RayAabbIntersect(float3 origin, float3 direction, float3 min, float3 max, out float t)
    {
        const float epsilon = 0.000001f;
        float tMin = 0f;
        float tMax = float.MaxValue;

        if (!RayAabbAxis(origin.x, direction.x, min.x, max.x, ref tMin, ref tMax, epsilon) ||
            !RayAabbAxis(origin.y, direction.y, min.y, max.y, ref tMin, ref tMax, epsilon) ||
            !RayAabbAxis(origin.z, direction.z, min.z, max.z, ref tMin, ref tMax, epsilon))
        {
            t = 0f;
            return false;
        }

        if (tMax < 0f)
        {
            t = 0f;
            return false;
        }

        t = tMin >= 0f ? tMin : tMax;
        return t >= 0f;
    }

    private static bool RayAabbAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax, float epsilon)
    {
        if (MathF.Abs(direction) <= epsilon) return origin >= min && origin <= max;

        float inverse = 1f / direction;
        float t0 = (min - origin) * inverse;
        float t1 = (max - origin) * inverse;
        if (t0 > t1) (t0, t1) = (t1, t0);
        if (t0 > tMin) tMin = t0;
        if (t1 < tMax) tMax = t1;
        return tMin <= tMax;
    }

    private static float3 AbsVector(float3 v) => new(MathF.Abs(v.x), MathF.Abs(v.y), MathF.Abs(v.z));

    private void PositionBeam(float3 origin, float3 direction, float length, float3 endPoint, float delta)
    {
        if (_beamSlot == null || _beamMesh == null) return;

        _beamSlot.GlobalPosition = origin;
        _beamSlot.GlobalRotation = floatQ.Identity;
        _beamSlot.GlobalScale = float3.One;

        float3 localDirect = _beamSlot.GlobalDirectionToLocal(direction);
        if (localDirect.Length > 0.0001f)
        {
            localDirect = localDirect.Normalized * length;
        }
        else
        {
            localDirect = float3.Backward * length;
        }

        float3 localActual = _beamSlot.GlobalPointToLocal(endPoint);
        // The exponential smoothing here damps real hand jitter in VR. On DESKTOP the mouse is already pixel
        // precise, so smoothing it just makes the cursor trail behind the pointer - the "sluggish / floaty"
        // feel. Snap 1:1 on desktop; keep the smoothing only for VR. -xlinka
        var rayInput = Engine.Current?.InputInterface;
        bool isDesktopPointer = rayInput != null && !rayInput.IsVRActive;
        if (!_hasVisualActualPoint || isDesktopPointer)
        {
            _visualActualPoint = localActual;
            _hasVisualActualPoint = true;
        }
        else
        {
            float t = 1f - MathF.Exp(-MathF.Max(VisualSmoothing.Value, 0f) * MathF.Max(delta, 0f));
            _visualActualPoint = float3.Lerp(_visualActualPoint, localActual, System.Math.Clamp(t, 0f, 1f));
        }

        _lastDirectHitDistance = length;
        SetIfChanged(_beamMesh.StartPoint, float3.Zero);
        SetIfChanged(_beamMesh.DirectTargetPoint, localDirect);
        SetIfChanged(_beamMesh.ActualTargetPoint, _visualActualPoint);

        if (_pointSlot != null)
        {
            SetIfChanged(_pointSlot.LocalPosition, _visualActualPoint);
        }

        if (_directCursorSlot != null)
        {
            SetIfChanged(_directCursorSlot.LocalPosition, localDirect);
        }

        if (_directLineMesh != null)
        {
            SetIfChanged(_directLineMesh.Radius, BeamRadius.Value * 0.75f);
            SetIfChanged(_directLineMesh.PointA, localDirect);
            SetIfChanged(_directLineMesh.PointB, _visualActualPoint);
        }
    }

    internal Slot? FindHeadSlot()
    {
        // Walk parent ActiveUserRoot directly - skips the per-ancestor GetComponent scan. - xlinka
        var headSlot = Slot?.Parent?.ActiveUserRoot?.HeadSlot;
        if (headSlot != null) return headSlot;

        // Fallback: legacy ancestor walk for nested cases without registered UserRoot.
        var current = Slot?.Parent;
        while (current != null)
        {
            var userRoot = current.GetComponent<UserRoot>();
            if (userRoot != null)
            {
                var hs = userRoot.HeadSlot;
                if (hs != null) return hs;
                break;
            }
            current = current.Parent;
        }

        current = Slot!.Parent;
        while (current != null)
        {
            if (current.Name.Value == "Body Nodes")
            {
                foreach (var child in current.Children)
                {
                    if (child.Name.Value == "Head") return child;
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private float2 ReadPointerAxis()
    {
        var input = Engine.Current?.InputInterface;
        if (input == null) return float2.Zero;

        if (!input.IsVRActive)
        {
            if (ControllerSide.Value != Chirality.Right)
            {
                return float2.Zero;
            }

            float scroll = input.Mouse?.ScrollWheelDelta.Value ?? 0f;
            return scroll == 0f ? float2.Zero : new float2(0f, scroll);
        }

        var controller = ControllerSide.Value == Chirality.Left
            ? input.LeftController
            : input.RightController;
        if (controller == null) return float2.Zero;

        float x = ApplyDeadzone(controller.ThumbstickPosition.X, 0.20f);
        float y = ApplyDeadzone(controller.ThumbstickPosition.Y, 0.20f);
        return new float2(x, y);
    }

    private bool ReadSecondaryPressed()
    {
        var input = Engine.Current?.InputInterface;
        if (input == null) return false;

        if (input.IsVRActive)
        {
            return ControllerSide.Value == Chirality.Left
                ? input.LeftController.SecondaryButtonPressed
                : input.RightController.SecondaryButtonPressed;
        }

        return false;
    }

    private static float ApplyDeadzone(float value, float deadzone)
    {
        return MathF.Abs(value) < deadzone ? 0f : value;
    }

    private static bool RaySphereIntersect(float3 origin, float3 direction, float3 center, float radius, out float t)
    {
        float3 oc = new(origin.x - center.x, origin.y - center.y, origin.z - center.z);
        float b = float3.Dot(oc, direction);
        float c = float3.Dot(oc, oc) - radius * radius;
        float disc = b * b - c;

        if (disc < 0f) { t = 0f; return false; }

        float sqrtDisc = MathF.Sqrt(disc);
        float nearT = -b - sqrtDisc;
        float farT = -b + sqrtDisc;

        if (nearT > 0f) { t = nearT; return true; }
        if (farT > 0f) { t = farT; return true; }

        t = 0f;
        return false;
    }

    private bool IsSlotOnThisHierarchy(Slot candidate)
    {
        var current = candidate;
        while (current != null)
        {
            if (ReferenceEquals(current, Slot)) return true;
            if (_ignoreRoot != null && ReferenceEquals(current, _ignoreRoot)) return true;
            current = current.Parent;
        }
        return false;
    }

    private static floatQ AlignYToDirection(float3 direction)
    {
        float len = direction.Length;
        if (len < 0.0001f) return floatQ.Identity;

        float3 dir = new(direction.x / len, direction.y / len, direction.z / len);
        float3 axis = float3.Cross(float3.Up, dir);
        float axisLen = axis.Length;

        if (axisLen < 0.001f)
        {
            return float3.Dot(float3.Up, dir) > 0f
                ? floatQ.Identity
                : floatQ.AxisAngle(float3.Forward, MathF.PI);
        }

        float3 normAxis = new(axis.x / axisLen, axis.y / axisLen, axis.z / axisLen);
        float angle = MathF.Acos(System.Math.Clamp(float3.Dot(float3.Up, dir), -1f, 1f));
        return floatQ.AxisAngle(normAxis, angle);
    }
}
