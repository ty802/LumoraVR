// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Gizmos;

[GizmoForComponent(typeof(Slot))]
public class SlotGizmo : ImplementableComponent, IGizmo, IGizmoDragHost
{
    public const float BUTTON_SIZE = 0.025f;

    public const float BUTTONS_OFFSET = 0.025f;

    public const float BUTTON_SEPARATION = 0.005f;

    public readonly SyncRef<Slot> TargetSlotRef = null!;

    // synced so every peer can tell whose selection it is and the authority can clear it when that user
    // leaves; never persisted - editor chrome, and the owning user doesn't survive the session either
    [NonPersistent]
    public readonly SyncRef<User> Owner = null!;

    public readonly Sync<bool> Active = null!;

    public readonly Sync<bool> IsFolded = null!;

    public readonly Sync<bool> IsLocalSpace = null!;

    // 0=translate, 1=rotate, 2=scale
    public readonly Sync<int> ActiveMode = null!;

    public readonly SyncRef<Component> LinkedInspector = null!;

    public event Action<int> OnModeChanged = null!;

    public event Action<bool> OnSpaceChanged = null!;

    public event Action OnOpenParentRequested = null!;

    public Slot TargetSlot => (TargetSlotRef?.Target) ?? null!;

    bool IGizmo.IsActive
    {
        get => Active?.Value ?? false;
        set
        {
            if (Active != null)
                Active.Value = value;
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();
        TargetSlotRef.OnTargetChange += _ => HandleTargetChanged();
        Owner.OnTargetChange += _ => WorldGizmos.For(World)?.ReindexOwner(this);
        Active.OnChanged += OnActiveChanged;
        IsFolded.OnChanged += OnFoldedChanged;
        IsLocalSpace.OnChanged += OnLocalSpaceChanged;
        ActiveMode.OnChanged += OnActiveModeChanged;
    }

    public override void OnInit()
    {
        base.OnInit();
        Active.Value = true;
        IsLocalSpace.Value = true;
    }

    private void OnActiveChanged(bool newValue)
    {
        ApplyChromeVisibility();
        NotifyChanged();
    }

    private void OnFoldedChanged(bool newValue)
    {
        ApplyChromeVisibility();
        NotifyChanged();
    }

    private void OnLocalSpaceChanged(bool newValue)
    {
        FollowTarget();
        OnSpaceChanged?.Invoke(newValue);
    }

    private void OnActiveModeChanged(int newMode)
    {
        ApplyChromeVisibility();
        OnModeChanged?.Invoke(newMode);
    }

    // The slot whose events we're wired to (kept separately so a re-target can unwire cleanly).
    private Slot? _watchedTarget;

    // Only the peer that spawned or re-targeted this gizmo builds its content. Everywhere else the
    // rig arrives over the wire, and a second peer adding its own "Bounds" slot would just duplicate
    // it, so remote peers resolve the chrome by name and wait if it has not landed yet. -xlinka
    private bool _buildsChrome;

    public void Setup(Slot targetSlot) => Setup(targetSlot, World?.LocalUser);

    public void Setup(Slot targetSlot, User? owner)
    {
        if (targetSlot == null || targetSlot.IsRootSlot)
            return;

        _buildsChrome = true;
        Owner.Target = owner!;
        BuildHandles(targetSlot);
        TargetSlotRef.Target = targetSlot;
        HandleTargetChanged();
    }

    // runs on every peer, including ones that only received this gizmo, so the outline is live everywhere
    private void HandleTargetChanged()
    {
        var target = TargetSlotRef?.Target;
        if (ReferenceEquals(target, _watchedTarget))
            return;

        var previous = _watchedTarget;
        UnsubscribeTarget();
        UnsubscribeBoundsSources();
        var registry = WorldGizmos.For(World);
        if (previous != null)
            registry?.Untrack(previous);

        if (target == null || target.IsDestroyed)
        {
            _boundsAnimated = false;
            NotifyChanged();
            return;
        }

        SubscribeTarget(target);
        registry?.Track(target, this);
        EnsureChromeSlots();
        RecomputeBounds();
        FollowTarget();
        ApplyChromeVisibility();
        NotifyChanged();
    }

    // MANIPULATION HANDLES: a combined rig of three translate arrows, three rotation rings and a
    // center uniform-scale cube, all driving the TARGET's transform fields through the laser + grab
    // system (grip a handle, drag, release = one undo step). Built as plain engine content (procedural
    // meshes + overlay materials) under the gizmo slot, so it inherits the gizmo's follow/rotation and
    // needs no hook work. Sizes are authored for ~1m viewing and the root is distance-scaled so the
    // gizmo stays usable far away.
    //
    // Nothing here carries a collision shape. Every handle sits inside or on top of the thing it edits,
    // so a physics ray can only ever come back with the object, and a shape that can never be the first
    // hit is a broadphase entry paying for nothing. Each handle answers the beam itself instead - a box
    // in its own local space, a ring for the rotation handles - and asks the laser to prefer it over
    // whatever is in front. Eleven trigger boxes per gizmo, rebuilt on every selection change, gone.
    // -xlinka
    private Slot? _handlesRoot;
    private Slot? _translateGroup;
    private Slot? _rotateGroup;
    private Slot? _scaleGroup;
    private readonly List<TransformHandle> _handles = new();

    // Sized to read comfortably at 1m: 0.15 total arrow, 0.25 ring, 0.05 cube.
    private const float ArrowShaftRadius = 0.007f;
    private const float ArrowShaftLength = 0.12f;
    private const float ArrowTipLength = 0.03f;
    private const float RingRadius = 0.25f;

    // counter the handles bump on begin/end rather than a scan of the rig, so "is this gizmo busy" is a
    // field read from any caller
    private int _draggingHandles;

    // chrome (the name label) hides while true, so it doesn't sit in the way
    public bool IsInteracting => _draggingHandles > 0;

    public void NotifyHandleDrag(bool dragging)
    {
        int previous = _draggingHandles;
        _draggingHandles = System.Math.Max(0, dragging ? previous + 1 : previous - 1);
        // Only the 0 <-> 1 crossing changes what is drawn, so that is the only time the hook hears.
        if ((previous > 0) != (_draggingHandles > 0))
            NotifyChanged();
    }

    private void BuildHandles(Slot? target)
    {
        if (_handlesRoot != null && !_handlesRoot.IsRemoved)
            return;
        if (target == null || Slot == null)
            return;

        var root = Slot.AddSlot("Handles");
        _handlesRoot = root;

        var chrome = WorldGizmos.For(World);
        var xMat = chrome?.GetMaterial(World, GizmoMaterialKind.AxisX);
        var yMat = chrome?.GetMaterial(World, GizmoMaterialKind.AxisY);
        var zMat = chrome?.GetMaterial(World, GizmoMaterialKind.AxisZ);
        var centerMat = chrome?.GetMaterial(World, GizmoMaterialKind.Center);

        // One MODE visible at a time (translate / rotate / scale) - showing all three at once reads as
        // clutter; ActiveMode gates the groups in ApplyChromeVisibility.
        _translateGroup = root.AddSlot("Translate");
        _rotateGroup = root.AddSlot("Rotate");
        _scaleGroup = root.AddSlot("Scale");

        // Orientation maps the handle's local +Y (all geometry is authored along +Y) onto each axis.
        var xAxis = new float3(1f, 0f, 0f);
        var yAxis = new float3(0f, 1f, 0f);
        var zAxis = new float3(0f, 0f, 1f);
        var yToX = floatQ.AxisAngleRad(zAxis, -MathF.PI / 2f);
        var yToZ = floatQ.AxisAngleRad(xAxis, MathF.PI / 2f);

        // TRANSLATE: three axis arrows, three plane pads (slide in the plane between two arrows), and
        // a free-drag cube at the center (view-plane translation).
        BuildArrow(_translateGroup, target, "X", xAxis, yToX, xMat);
        BuildArrow(_translateGroup, target, "Y", yAxis, floatQ.Identity, yMat);
        BuildArrow(_translateGroup, target, "Z", zAxis, yToZ, zMat);
        BuildPlanePad(_translateGroup, target, "YZ", xAxis, yToX, chrome?.GetMaterial(World, GizmoMaterialKind.PlaneYZ));
        BuildPlanePad(_translateGroup, target, "XZ", yAxis, floatQ.Identity, chrome?.GetMaterial(World, GizmoMaterialKind.PlaneXZ));
        BuildPlanePad(_translateGroup, target, "XY", zAxis, yToZ, chrome?.GetMaterial(World, GizmoMaterialKind.PlaneXY));
        BuildFreeCube(_translateGroup, target, chrome?.GetMaterial(World, GizmoMaterialKind.FreeMove));

        // ROTATE: three torus rings.
        BuildRing(_rotateGroup, target, "X", xAxis, yToX, xMat);
        BuildRing(_rotateGroup, target, "Y", yAxis, floatQ.Identity, yMat);
        BuildRing(_rotateGroup, target, "Z", zAxis, yToZ, zMat);

        // SCALE: center uniform cube plus per-axis cubes on thin connectors.
        BuildScaleCube(_scaleGroup, target, centerMat);
        BuildAxisScaleCube(_scaleGroup, target, "X", 0, xAxis, yToX, xMat);
        BuildAxisScaleCube(_scaleGroup, target, "Y", 1, yAxis, floatQ.Identity, yMat);
        BuildAxisScaleCube(_scaleGroup, target, "Z", 2, zAxis, yToZ, zMat);
    }

    private static void AttachVisual(Slot slot, Meshes.ProceduralMesh mesh, Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        if (material != null)
            renderer.Material.Target = material;
    }

    private void BuildArrow(Slot parent, Slot target, string name, float3 axis, floatQ orientation,
        Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var handle = parent.AddSlot($"Translate{name}");
        handle.LocalRotation.Value = orientation;

        var component = handle.AttachComponent<TranslateHandle>();
        component.TargetSlot.Target = target;
        component.AxisReference.Target = Slot;
        component.LocalAxis.Value = axis;
        _handles.Add(component);

        // Pick box is a hair fatter than the shaft so a thin arrow is still catchable, and it spans
        // shaft plus tip from where the mesh starts.
        component.SetPickBox(
            new float3(0f, 0.02f + (ArrowShaftLength + ArrowTipLength) * 0.5f, 0f),
            new float3(0.045f, ArrowShaftLength + ArrowTipLength, 0.045f));

        // Single arrow mesh (shaft + tip in one), authored along +Y from its origin.
        var meshSlot = handle.AddSlot("Arrow");
        meshSlot.LocalPosition.Value = new float3(0f, 0.02f, 0f);
        var arrow = meshSlot.AttachComponent<Meshes.ArrowMesh>();
        arrow.ShaftRadius.Value = ArrowShaftRadius;
        arrow.ShaftLength.Value = ArrowShaftLength;
        arrow.TipRadius.Value = 0.02f;
        arrow.TipLength.Value = ArrowTipLength;
        arrow.Segments.Value = 12;
        AttachVisual(meshSlot, arrow, material);
    }

    // Small square between two arrows: drags the target in that plane (axis = the plane's NORMAL).
    private void BuildPlanePad(Slot parent, Slot target, string name, float3 normal, floatQ orientation,
        Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var handle = parent.AddSlot($"Plane{name}");
        handle.LocalRotation.Value = orientation; // local +Y = plane normal; the pad lies in local XZ

        var component = handle.AttachComponent<PlaneTranslateHandle>();
        component.TargetSlot.Target = target;
        component.AxisReference.Target = Slot;
        component.LocalAxis.Value = normal;
        _handles.Add(component);

        var padSlot = handle.AddSlot("Pad");
        padSlot.LocalPosition.Value = new float3(0.0375f, 0f, 0.0375f);
        var mesh = padSlot.AttachComponent<Meshes.BoxMesh>();
        mesh.Size.Value = new float3(0.025f, 0.004f, 0.025f);
        AttachVisual(padSlot, mesh, material);

        // The pad rides a child slot, so its offset folds into the handle-local pick box.
        component.SetPickBox(padSlot.LocalPosition.Value, new float3(0.03f, 0.012f, 0.03f));
    }

    private void BuildFreeCube(Slot parent, Slot target, Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var handle = parent.AddSlot("FreeMove");

        var component = handle.AttachComponent<FreeTranslateHandle>();
        component.TargetSlot.Target = target;
        component.AxisReference.Target = Slot;
        _handles.Add(component);

        var mesh = handle.AttachComponent<Meshes.BoxMesh>();
        mesh.Size.Value = float3.One * 0.025f;
        AttachVisual(handle, mesh, material);

        component.SetPickBox(float3.Zero, float3.One * 0.04f);
    }

    private void BuildRing(Slot parent, Slot target, string name, float3 axis, floatQ orientation,
        Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var ring = parent.AddSlot($"Rotate{name}");
        ring.LocalRotation.Value = orientation;

        var component = ring.AttachComponent<RotateHandle>();
        component.TargetSlot.Target = target;
        component.AxisReference.Target = Slot;
        component.LocalAxis.Value = axis;
        component.Radius.Value = RingRadius;
        _handles.Add(component);

        // One torus for the visual (rings lie in the mesh's local XZ plane around +Y, matching the
        // ring slot's orientation). No pick box: a circle is not a box, so RotateHandle overrides the
        // base test with ray-vs-ring off its own Radius.
        var torus = ring.AttachComponent<Meshes.TorusMesh>();
        torus.MajorRadius.Value = RingRadius;
        torus.MinorRadius.Value = 0.004f;
        torus.MajorSegments.Value = 48;
        torus.MinorSegments.Value = 8;
        AttachVisual(ring, torus, material);
    }

    // Axis-scale cube: a small cube out along the axis on a thin connector; dragging it stretches
    // that ONE local-scale component.
    private void BuildAxisScaleCube(Slot parent, Slot target, string name, int axisIndex, float3 axis,
        floatQ orientation, Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var handle = parent.AddSlot($"Scale{name}");
        handle.LocalRotation.Value = orientation;

        var component = handle.AttachComponent<AxisScaleHandle>();
        component.TargetSlot.Target = target;
        component.AxisReference.Target = Slot;
        component.LocalAxis.Value = axis;
        component.ScaleAxisIndex.Value = axisIndex;
        _handles.Add(component);

        var connectorSlot = handle.AddSlot("Connector");
        connectorSlot.LocalPosition.Value = new float3(0f, 0.075f, 0f);
        var connector = connectorSlot.AttachComponent<Meshes.CylinderMesh>();
        connector.Radius.Value = 0.0015f;
        connector.Height.Value = 0.15f;
        connector.Segments.Value = 6;
        AttachVisual(connectorSlot, connector, material);

        var cubeSlot = handle.AddSlot("Cube");
        cubeSlot.LocalPosition.Value = new float3(0f, 0.15f, 0f);
        var cube = cubeSlot.AttachComponent<Meshes.BoxMesh>();
        cube.Size.Value = float3.One * 0.025f;
        AttachVisual(cubeSlot, cube, material);

        // Only the cube is grabbable; the connector is a line to look at, not to aim for.
        component.SetPickBox(cubeSlot.LocalPosition.Value, float3.One * 0.04f);
    }

    private void BuildScaleCube(Slot parent, Slot target, Lumora.Core.Assets.OverlayUnlitMaterial? material)
    {
        var handle = parent.AddSlot("ScaleUniform");

        var component = handle.AttachComponent<ScaleHandle>();
        component.TargetSlot.Target = target;
        component.AxisReference.Target = Slot;
        _handles.Add(component);

        var mesh = handle.AttachComponent<Meshes.BoxMesh>();
        mesh.Size.Value = float3.One * 0.05f;
        AttachVisual(handle, mesh, material);

        component.SetPickBox(float3.Zero, float3.One * 0.07f);
    }

    // CHROME VISIBILITY: which mode's handles are showing. Driven by the fields that decide it
    // (Active / IsFolded / ActiveMode), never polled - a same-value write to a Sync fires nothing, so
    // the mode switch calls this directly instead of waiting for a change event that may not come.
    private bool _chromeVisible;

    private void ApplyChromeVisibility()
    {
        // Read the flag first: a peer that only RECEIVED this gizmo has no handles root of its own, and
        // it still needs to know whether there is a box on screen (the bounds clock asks).
        bool visible = Active.Value && !IsFolded.Value;
        _chromeVisible = visible;

        var root = _handlesRoot;
        if (root == null || root.IsRemoved)
            return;

        if (root.ActiveSelf.Value != visible)
            root.ActiveSelf.Value = visible;

        // One mode at a time: 0=translate, 1=rotate, 2=scale.
        int mode = ActiveMode.Value;
        SetGroupActive(_translateGroup, visible && mode == 0);
        SetGroupActive(_rotateGroup, visible && mode == 1);
        SetGroupActive(_scaleGroup, visible && mode == 2);
    }

    private static void SetGroupActive(Slot? group, bool active)
    {
        if (group != null && !group.IsRemoved && group.ActiveSelf.Value != active)
            group.ActiveSelf.Value = active;
    }

    // CONSTANT APPARENT SIZE: scale = max(user scale, viewer distance), so editor chrome stays usable
    // whether the thing is at arm's length or across the room. Rewriting that scale every frame is a
    // datamodel write (and a sync delta) every frame for a value that barely moves, so it only lands
    // when the viewer has actually closed or opened the distance by a couple of percent. Below that
    // the difference is under a pixel. -xlinka
    private const float ScaleDeadband = 0.02f;
    private float _chromeDistance = -1f;
    private float _chromeUserScale = -1f;
    private Slot? _headSlot;

    private void UpdateChromeScale()
    {
        var root = _handlesRoot;
        if (root == null || root.IsRemoved)
            return;

        var userRoot = World?.LocalUser?.Root;
        if (userRoot == null)
        {
            _headSlot = null;
            return;
        }
        if (_headSlot == null || _headSlot.IsDestroyed)
            _headSlot = userRoot.HeadSlot;
        if (_headSlot == null)
            return;

        var delta = _headSlot.GlobalPosition - Slot.GlobalPosition;
        float distance = MathF.Sqrt(delta.LengthSquared);
        float userScale = userRoot.Slot?.GlobalScale.x ?? 1f;

        if (_chromeDistance > 0f
            && MathF.Abs(distance - _chromeDistance) <= _chromeDistance * ScaleDeadband
            && MathF.Abs(userScale - _chromeUserScale) <= _chromeUserScale * ScaleDeadband)
            return;

        _chromeDistance = distance;
        _chromeUserScale = userScale;
        root.LocalScale.Value = float3.One * MathF.Max(userScale, distance);
    }

    // BOUNDS: measured ONCE in the target's own local space and cached. Local space is the whole
    // point - a box expressed there does not change when the target moves, so dragging something
    // around re-measures nothing and the wireframe keeps up by riding a child slot whose transform
    // mirrors the target's. Walking a subtree of renderers per frame is what makes selection chrome
    // expensive on big objects, and none of it buys anything until the geometry actually changes.
    //
    // What DOES change it: a slot or component added or removed anywhere under the target
    // (SubtreeStructureChanged), or one of the components the measurement read reporting a change of
    // its own (a mesh swapped, an asset finishing its load, a procedural mesh resized). Recompute is
    // deferred to the next update and rate limited, so a burst of edits measures once. -xlinka
    // ANIMATED TARGETS: the events above catch every change that goes through the datamodel, and miss
    // the ones that do not. An animator moving child slots, a soft body rewriting vertices in place, a
    // skinned mesh on live bones - none of those add or remove anything, and none of them fire a
    // component change, so the cached box quietly goes stale while the object visibly moves out of it.
    // The only honest answer for those is a clock, and the measurement is a subtree walk, so it runs
    // slow (2 Hz) and only while the gizmo is actually on screen. Anything static keeps costing
    // nothing at all: the flag comes back false and the clock never arms. -xlinka
    private const float BoundsRecomputeInterval = 0.1f;
    private const float AnimatedBoundsInterval = 0.5f;
    private static readonly BoundingBox FallbackBounds = new(new float3(-0.1f, -0.1f, -0.1f), new float3(0.1f, 0.1f, 0.1f));

    private bool _boundsAnimated;
    private readonly List<Component> _boundsSources = new();
    private BoundingBox _localBounds = FallbackBounds;
    private bool _boundsDirty;
    private double _lastBoundsTime = double.NegativeInfinity;
    private int _boundsVersion;

    private Slot? _boundsRoot;
    private Slot? _labelRoot;
    private float3 _boundsLocalPosition = float3.Zero;
    private floatQ _boundsLocalRotation = floatQ.Identity;
    private float3 _boundsLocalScale = float3.One;
    private float3 _labelLocalPosition = float3.Zero;

    // cached; never computed per frame
    public BoundingBox LocalBounds => _localBounds;

    // bumped only when LocalBounds actually changed, so the visual can skip redrawing geometry that didn't move
    public int BoundsVersion => _boundsVersion;

    // the bounds wireframe hangs off this, so following the target costs nothing beyond the gizmo's own follow
    public Slot? BoundsRoot => _boundsRoot != null && !_boundsRoot.IsRemoved ? _boundsRoot : null;

    // unrotated and unscaled, for the name label
    public Slot? LabelRoot => _labelRoot != null && !_labelRoot.IsRemoved ? _labelRoot : null;

    public void InvalidateBounds() => _boundsDirty = true;

    private void RecomputeBounds()
    {
        _boundsDirty = false;
        _lastBoundsTime = World?.Time?.TotalTime ?? 0.0;

        UnsubscribeBoundsSources();
        var target = TargetSlotRef.Target;
        if (target == null || target.IsDestroyed)
        {
            _boundsAnimated = false;
            return;
        }

        if (!SlotBoundsHelper.TryComputeLocalBounds(target, out var bounds, _boundsSources, out _boundsAnimated))
        {
            bounds = FallbackBounds;
            _boundsSources.Clear();
        }
        SubscribeBoundsSources();

        if (!SameBox(bounds, _localBounds))
        {
            _localBounds = bounds;
            _boundsVersion++;
            NotifyChanged();
        }
        SyncBoundsTransform();
    }

    private static bool SameBox(in BoundingBox a, in BoundingBox b)
        => Approximately(a.Min, b.Min) && Approximately(a.Max, b.Max);

    private static bool Approximately(in float3 a, in float3 b)
        => MathF.Abs(a.x - b.x) < 1e-5f && MathF.Abs(a.y - b.y) < 1e-5f && MathF.Abs(a.z - b.z) < 1e-5f;

    private void SubscribeBoundsSources()
    {
        for (int i = 0; i < _boundsSources.Count; i++)
            _boundsSources[i].Changed += OnBoundsSourceChanged;
    }

    private void UnsubscribeBoundsSources()
    {
        for (int i = 0; i < _boundsSources.Count; i++)
        {
            var source = _boundsSources[i];
            if (source != null)
                source.Changed -= OnBoundsSourceChanged;
        }
        _boundsSources.Clear();
    }

    private void OnBoundsSourceChanged(IChangeable changed) => _boundsDirty = true;

    // creates them only on the peer that owns the build (_buildsChrome)
    private bool EnsureChromeSlots()
    {
        var gizmoSlot = Slot;
        if (gizmoSlot == null || gizmoSlot.IsRemoved)
            return false;
        if (_boundsRoot is { IsRemoved: false } && _labelRoot is { IsRemoved: false })
            return true;

        _boundsRoot = gizmoSlot.FindChild("Bounds", recursive: false);
        _labelRoot = gizmoSlot.FindChild("Label", recursive: false);
        if (_buildsChrome)
        {
            _boundsRoot ??= gizmoSlot.AddSlot("Bounds");
            _labelRoot ??= gizmoSlot.AddSlot("Label");
        }
        if (_boundsRoot == null || _labelRoot == null)
            return false;

        // Force the first write through: what the slots carry right now is whatever they were left
        // with, not what this target needs.
        _boundsLocalPosition = new float3(float.NaN, 0f, 0f);
        _labelLocalPosition = new float3(float.NaN, 0f, 0f);
        _boundsLocalRotation = new floatQ(float.NaN, 0f, 0f, 1f);
        _boundsLocalScale = new float3(float.NaN, 1f, 1f);
        return true;
    }

    // every write is guarded: in local space mode the offsets are constant, so moving or spinning the
    // target writes nothing here at all
    private void SyncBoundsTransform()
    {
        var target = TargetSlotRef?.Target;
        var gizmoSlot = Slot;
        if (target == null || target.IsDestroyed || gizmoSlot == null)
            return;
        if (!EnsureChromeSlots())
            return;
        var boundsRoot = _boundsRoot!;

        var position = gizmoSlot.GlobalPointToLocal(target.GlobalPosition);
        var rotation = gizmoSlot.GlobalRotationToLocal(target.GlobalRotation);
        var scale = gizmoSlot.GlobalScaleToLocal(target.GlobalScale);

        if (!Approximately(position, _boundsLocalPosition))
        {
            _boundsLocalPosition = position;
            boundsRoot.LocalPosition.Value = position;
        }
        if (!Approximately(rotation, _boundsLocalRotation))
        {
            _boundsLocalRotation = rotation;
            boundsRoot.LocalRotation.Value = rotation;
        }
        if (!Approximately(scale, _boundsLocalScale))
        {
            _boundsLocalScale = scale;
            boundsRoot.LocalScale.Value = scale;
        }

        var labelRoot = _labelRoot!;
        var center = _localBounds.Center;
        var top = new float3(center.x, _localBounds.Max.y, center.z);
        var labelPosition = gizmoSlot.GlobalPointToLocal(target.LocalPointToGlobal(top));
        if (!Approximately(labelPosition, _labelLocalPosition))
        {
            _labelLocalPosition = labelPosition;
            labelRoot.LocalPosition.Value = labelPosition;
        }
    }

    private static bool Approximately(in floatQ a, in floatQ b)
        => MathF.Abs(a.x - b.x) < 1e-5f && MathF.Abs(a.y - b.y) < 1e-5f
        && MathF.Abs(a.z - b.z) < 1e-5f && MathF.Abs(a.w - b.w) < 1e-5f;

    // LIVE TRACKING: the gizmo is event-driven, not polled. WorldTransformChanged fires AFTER all
    // component updates (coalesced once per frame, covers own moves + ancestor moves + scale), so the
    // follow never lags a frame behind whatever moved the target. A move does NOT re-render the
    // visual: the wireframe is drawn in the bounds slot's own space and rides the hierarchy. Only a
    // rename (label text) and a shape change (box geometry) reach the hook. -xlinka
    private void SubscribeTarget(Slot target)
    {
        UnsubscribeTarget();
        _watchedTarget = target;
        target.WorldTransformChanged += OnTargetTransformChanged;
        target.SubtreeStructureChanged += OnTargetStructureChanged;
        target.SlotName.OnChanged += OnTargetRenamed;
        target.OnPrepareDestroy += OnTargetDestroyed;
    }

    private void UnsubscribeTarget()
    {
        if (_watchedTarget == null)
            return;
        _watchedTarget.WorldTransformChanged -= OnTargetTransformChanged;
        _watchedTarget.SubtreeStructureChanged -= OnTargetStructureChanged;
        _watchedTarget.SlotName.OnChanged -= OnTargetRenamed;
        _watchedTarget.OnPrepareDestroy -= OnTargetDestroyed;
        _watchedTarget = null;
    }

    private void OnTargetTransformChanged(Slot slot) => FollowTarget();

    private void OnTargetStructureChanged(Slot slot) => _boundsDirty = true;

    private void OnTargetRenamed(string newName) => NotifyChanged(); // the name label re-reads the target

    private void OnTargetDestroyed(Slot slot) => DestroySelf();

    // the gizmo slot is the single transform writer; the hook only lays out local-space children under
    // it (two writers on the same node fought - rotation lost every round)
    private void FollowTarget()
    {
        var target = TargetSlotRef.Target;
        if (target == null || target.IsDestroyed || Slot == null)
            return;
        Slot.GlobalPosition = target.GlobalPosition;
        Slot.GlobalRotation = IsLocalSpace.Value ? target.GlobalRotation : floatQ.Identity;
        SyncBoundsTransform();
    }

    // Re-selecting the ALREADY-ACTIVE mode within this window resets that transform (tap Translate
    // twice = zero the position). Implemented here, not in OnActiveModeChanged - a same-value write
    // never fires the Sync change event.
    private const float ModeDoubleTapSeconds = 0.4f;
    private float _lastModeSwitchTime = -10f;
    private int _lastSwitchedMode = -1;

    // double-tap = reset position
    public void SwitchToTranslation() => SwitchMode(0);

    // double-tap = reset rotation
    public void SwitchToRotation() => SwitchMode(1);

    // double-tap = reset scale
    public void SwitchToScale() => SwitchMode(2);

    private void SwitchMode(int mode)
    {
        float now = (float)(World?.Time?.TotalTime ?? 0.0);
        bool doubleTap = mode == _lastSwitchedMode && now - _lastModeSwitchTime < ModeDoubleTapSeconds;
        _lastSwitchedMode = mode;
        _lastModeSwitchTime = now;

        ActiveMode.Value = mode;
        ApplyChromeVisibility(); // same-value writes don't fire OnChanged; make the switch visible now

        if (!doubleTap)
            return;
        var target = TargetSlotRef.Target;
        if (target == null || target.IsDestroyed)
            return;
        // Undoable reset: the Reset* methods are plain writes, so wrap the pose ourselves.
        var undo = SlotTransformUndoBatch.Begin(target, mode switch
        {
            0 => "Reset Position",
            1 => "Reset Rotation",
            _ => "Reset Scale",
        });
        switch (mode)
        {
            case 0: ResetPosition(); break;
            case 1: ResetRotation(); break;
            default: ResetScale(); break;
        }
        InspectorUndo.Record(this, undo?.Commit());
    }

    public void ToggleSpace()
    {
        IsLocalSpace.Value = !IsLocalSpace.Value;
    }

    public void ToggleFolded()
    {
        IsFolded.Value = !IsFolded.Value;
    }

    public void OpenParent()
    {
        var parent = TargetSlotRef.Target?.Parent;
        if (parent == null || parent.IsRootSlot)
            return;

        OnOpenParentRequested?.Invoke();

        var inspector = LinkedInspector.Target;
        // Re-point this rig instead of tearing it down and building another one for the slot one
        // level up; the handles and their materials are identical either way.
        Retarget(parent, Owner.Target);
        if (inspector != null)
            LinkedInspector.Target = inspector;
    }

    public void ResetPosition()
    {
        if (TargetSlotRef.Target != null)
        {
            TargetSlotRef.Target.LocalPosition.Value = float3.Zero;
        }
    }

    public void ResetRotation()
    {
        if (TargetSlotRef.Target != null)
        {
            TargetSlotRef.Target.LocalRotation.Value = floatQ.Identity;
        }
    }

    public void ResetScale()
    {
        if (TargetSlotRef.Target != null)
        {
            TargetSlotRef.Target.LocalScale.Value = float3.One;
        }
    }

    // POOLING: building the rig is the expensive half of a selection change, so a gizmo that is
    // dismissed parks itself instead of dying, and the next selection points the same slots at a new
    // target. Everything that names the old target has to be cleared on the way out or a parked rig
    // keeps a dead slot alive and its handles stay armed. -xlinka

    // ready for Retarget afterward
    internal void Park()
    {
        _draggingHandles = 0;
        LinkedInspector.Target = null!;
        TargetSlotRef.Target = null!; // the ref handler unwires the watches and untracks
        Owner.Target = null!;
        for (int i = 0; i < _handles.Count; i++)
        {
            if (_handles[i] is { IsDestroyed: false } handle)
                handle.TargetSlot.Target = null!;
        }

        Active.Value = false;
        IsFolded.Value = false;
        _chromeDistance = -1f;
        _chromeUserScale = -1f;
        if (Slot != null && !Slot.IsRemoved)
            Slot.ActiveSelf.Value = false;
    }

    // works on a fresh gizmo and on a parked one
    internal void Retarget(Slot target, User? owner)
    {
        if (target == null || target.IsRootSlot)
            return;

        _buildsChrome = true;
        if (Slot != null && !Slot.IsRemoved)
        {
            Slot.ActiveSelf.Value = true;
            Slot.Name.Value = $"Gizmo_{target.Name.Value}";
        }

        Owner.Target = owner!;
        Active.Value = true;
        IsFolded.Value = false;
        LinkedInspector.Target = null!;

        BuildHandles(target);
        for (int i = 0; i < _handles.Count; i++)
        {
            if (_handles[i] is { IsDestroyed: false } handle)
                handle.TargetSlot.Target = target;
        }

        TargetSlotRef.Target = target;
        HandleTargetChanged();
    }

    internal void DestroySelf()
    {
        UnsubscribeTarget();
        UnsubscribeBoundsSources();
        var registry = WorldGizmos.For(World);
        registry?.Untrack(TargetSlotRef?.RawTarget);
        registry?.Unpark(this);
        Slot?.Destroy();
    }

    public override void OnStart()
    {
        base.OnStart();
        // A gizmo that arrived over the wire never ran Setup: its target ref may already be resolved,
        // and its chrome slots may still be in flight.
        HandleTargetChanged();
        if (Slot != null)
            Slot.SubtreeStructureChanged += OnOwnStructureChanged;
    }

    private void OnOwnStructureChanged(Slot slot)
    {
        if (_boundsRoot is { IsRemoved: false } && _labelRoot is { IsRemoved: false })
            return;
        if (!EnsureChromeSlots())
            return;
        SyncBoundsTransform();
        NotifyChanged();
    }

    public override void OnUserLeft(User user)
    {
        // Only the authority prunes: every peer sees the departure, and a peer that tried would just
        // have the delete refused. RawTarget because the leaving user's ref already reads null.
        if (World?.IsAuthority == true && user != null && ReferenceEquals(user, Owner?.RawTarget))
            DestroySelf();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // The gizmo dies with its target. OnPrepareDestroy covers the normal path; this catches a slot
        // pulled out from under us without one. A null watch is a parked rig or a ref that has not
        // resolved yet, not a dead target - both just cost nothing until something changes.
        var target = _watchedTarget;
        if (target == null)
            return;
        if (target.IsDestroyed)
        {
            DestroySelf();
            return;
        }

        double now = World?.Time?.TotalTime ?? 0.0;
        // A target that deforms without telling anyone gets re-measured on the slow clock, but only
        // while there is a box on screen to be wrong.
        if (_boundsAnimated && _chromeVisible && now - _lastBoundsTime >= AnimatedBoundsInterval)
            _boundsDirty = true;
        if (_boundsDirty && now - _lastBoundsTime >= BoundsRecomputeInterval)
            RecomputeBounds();

        // A folded or hidden gizmo has nothing on screen to size, so it costs nothing per frame.
        if (_chromeVisible)
            UpdateChromeScale();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // Space-mode toggles change which rotation the gizmo mirrors; re-follow on any field change.
        FollowTarget();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.SubtreeStructureChanged -= OnOwnStructureChanged;
        UnsubscribeTarget();
        UnsubscribeBoundsSources();
        var registry = WorldGizmos.For(World);
        registry?.Untrack(TargetSlotRef?.RawTarget);
        registry?.Unpark(this);
        base.OnDestroy();
    }
}
