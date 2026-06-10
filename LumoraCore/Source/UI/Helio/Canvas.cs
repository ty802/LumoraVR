
// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Helio.UI;

// root of a UI tree. attach to a slot to make it (and descendants) a UI subtree. - xlinka
public class Canvas : Component, ILaserPointerTarget, ILaserAxisTarget, ILaserSecondaryTarget
{
    private sealed class PointerState
    {
        public IUIInteractable? Hovered;
        public IUIInteractable? Pressed;
        public UIInteractionContext LastContext;
        public bool IsPressed;
    }

    private readonly Dictionary<int, PointerState> _pointers = new();
    private RectTransform? _rootRect;
    private GraphicChunkRoot? _chunkRoot;
    private GraphicsChunk? _rootChunk;

    // Per-chunk rendering: each GraphicChunkRoot below the canvas owns an independent mesh, so a
    // subtree that animates (e.g. the FPS sparkline) re-uploads only its own small mesh instead of
    // the whole canvas. The root chunk is everything not under a nested GraphicChunkRoot. - xlinka
    private readonly Dictionary<GraphicChunkRoot, GraphicsChunk> _chunkMap = new();
    private readonly Dictionary<GraphicChunkRoot, bool> _chunkBuilt = new();
    private readonly HashSet<GraphicChunkRoot> _dirtyChunks = new();
    private readonly HashSet<GraphicChunkRoot> _seenChunkRoots = new();
    private readonly List<GraphicChunkRoot> _chunkScratch = new();
    private bool _rootDirty = true;
    private bool _fullDirty = true;

    // Full refresh: rebuild the root chunk and every nested chunk. - xlinka
    public void MarkDirty() => _fullDirty = true;

    // Route a change to the chunk that owns it, so only that chunk re-renders/uploads. - xlinka
    public void MarkDirty(RectTransform rect)
    {
        var root = FindChunkRoot(rect.Slot);
        if (root == null)
            _rootDirty = true;
        else
            _dirtyChunks.Add(root);
    }

    public RectTransform? RootRectTransform => _rootRect;
    public GraphicChunkRoot? ChunkRoot => _chunkRoot;
    public GraphicsChunk? RootChunk => _rootChunk;
    public int InteractionTargetPriority => 1000;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        return new InteractionDescription
        {
            Name = Slot?.SlotName.Value,
            Cursor = LaserCursor.Default,
        };
    }

    public bool TryGetLaserPointerHit(
        InteractionLaser laser,
        in float3 rayOrigin,
        in float3 rayDirection,
        float maxDistance,
        out LaserPointerHit hit)
    {
        hit = default;
        var source = GetInteractionSource(laser);
        int pointerId = GetPointerId(laser);
        if (!TryHitTest(rayOrigin, rayDirection, source, pointerId, out var uiHit, GetInteractionActor(laser)))
        {
            return false;
        }

        if (uiHit.Context.Distance > maxDistance)
        {
            return false;
        }

        hit = new LaserPointerHit(uiHit.Context.Distance, uiHit.Context.WorldPoint);
        return true;
    }

    public void UpdateLaserPointer(
        InteractionLaser laser,
        int pointerId,
        in float3 rayOrigin,
        in float3 rayDirection,
        bool isPressed)
    {
        UpdatePointer(GetInteractionSource(laser), pointerId, rayOrigin, rayDirection, isPressed, GetInteractionActor(laser));
    }

    public void ClearLaserPointer(InteractionLaser laser, int pointerId)
    {
        ClearPointer(GetInteractionSource(laser), pointerId, GetInteractionActor(laser));
    }

    public bool ProcessLaserAxis(InteractionLaser laser, int pointerId, in float2 axis)
    {
        return ProcessAxis(GetInteractionSource(laser), pointerId, axis);
    }

    public bool TriggerLaserSecondary(InteractionLaser laser, int pointerId)
    {
        return TriggerSecondary(GetInteractionSource(laser), pointerId);
    }

    public UIHit? HitTest(float3 rayOrigin, float3 rayDirection)
    {
        return TryHitTest(rayOrigin, rayDirection, out var hit) ? hit : null;
    }

    public bool TryHitTest(float3 rayOrigin, float3 rayDirection, out UIHit hit)
    {
        return TryHitTest(rayOrigin, rayDirection, UIInteractionSource.Unknown, 0, out hit);
    }

    public bool TryHitTest(
        float3 rayOrigin,
        float3 rayDirection,
        UIInteractionSource source,
        int pointerId,
        out UIHit hit,
        User? actor = null)
    {
        hit = default;
        if (!TryRayToCanvasPoint(rayOrigin, rayDirection, out var context, source, pointerId, actor))
        {
            return false;
        }

        var candidate = default(HitCandidate);
        ScanHitSlot(Slot, in context, ref candidate, null);
        if (candidate.Interactable == null)
        {
            return false;
        }

        hit = new UIHit(candidate.Interactable, context);
        return true;
    }

    public void UpdatePointer(
        UIInteractionSource source,
        int pointerId,
        float3 rayOrigin,
        float3 rayDirection,
        bool isPressed,
        User? actor = null)
    {
        if (!TryRayToCanvasPoint(rayOrigin, rayDirection, out var context, source, pointerId, actor))
        {
            ClearPointer(source, pointerId, actor);
            return;
        }

        var candidate = default(HitCandidate);
        ScanHitSlot(Slot, in context, ref candidate, null);
        var hovered = candidate.Interactable;
        int key = PointerKey(source, pointerId);

        if (!_pointers.TryGetValue(key, out var state))
        {
            state = new PointerState();
            _pointers[key] = state;
        }
        state.LastContext = context;

        if (!ReferenceEquals(state.Hovered, hovered))
        {
            state.Hovered?.NotifyHoverExit(in context);
            state.Hovered = hovered;
            state.Hovered?.NotifyHoverEnter(in context);
        }

        if (isPressed && !state.IsPressed)
        {
            state.Pressed = hovered;
            state.Pressed?.NotifyPress(in context);
        }
        else if (isPressed && state.Pressed != null)
        {
            state.Pressed.NotifyDrag(in context);
        }
        else if (!isPressed && state.IsPressed)
        {
            var pressed = state.Pressed;
            pressed?.NotifyRelease(in context);

            if (pressed != null && ReferenceEquals(pressed, hovered))
            {
                pressed.NotifySubmit(in context);
            }

            state.Pressed = null;
        }

        state.IsPressed = isPressed;
    }

    public bool ProcessAxis(UIInteractionSource source, int pointerId, in float2 axis)
    {
        if (axis == float2.Zero)
        {
            return false;
        }

        int key = PointerKey(source, pointerId);
        if (!_pointers.TryGetValue(key, out var state) || state.Hovered == null)
        {
            return false;
        }

        return DispatchAxis(state.Hovered, in state.LastContext, in axis);
    }

    public bool TriggerSecondary(UIInteractionSource source, int pointerId)
    {
        int key = PointerKey(source, pointerId);
        if (!_pointers.TryGetValue(key, out var state) || state.Hovered == null)
        {
            return false;
        }

        return DispatchSecondary(state.Hovered, in state.LastContext);
    }

    public void ClearPointer(UIInteractionSource source, int pointerId, User? actor = null)
    {
        int key = PointerKey(source, pointerId);
        if (!_pointers.TryGetValue(key, out var state))
        {
            return;
        }

        var context = new UIInteractionContext(this, source, pointerId, float2.Zero, float3.Zero, float3.Zero, float3.Zero, 0f, actor);
        state.Hovered?.NotifyHoverExit(in context);
        state.Pressed?.NotifyRelease(in context);
        _pointers.Remove(key);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureRoot();
    }

    public override void OnEnabled()
    {
        base.OnEnabled();
        _fullDirty = true;
    }

    public void EnsureRoot()
    {
        _rootRect ??= Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        _chunkRoot ??= Slot.GetComponent<GraphicChunkRoot>() ?? Slot.AttachComponent<GraphicChunkRoot>();
        if (_rootChunk == null)
        {
            _rootChunk = new GraphicsChunk(this, _rootRect);
            _rootChunk.PrepareCompute();
            _rootChunk.SubmitChanges();
        }
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (!_rootDirty && !_fullDirty && _dirtyChunks.Count == 0) return;
        RebuildGraphics();
    }

    public override void OnDestroy()
    {
        foreach (var pointer in _pointers)
        {
            var context = new UIInteractionContext(this, UIInteractionSource.Unknown, pointer.Key, float2.Zero, float3.Zero, float3.Zero, float3.Zero, 0f);
            pointer.Value.Hovered?.NotifyHoverExit(in context);
            pointer.Value.Pressed?.NotifyRelease(in context);
        }
        _pointers.Clear();
        base.OnDestroy();
    }

    // TEMP perf instrumentation: throttled phase timing to find the rebuild bottleneck. - xlinka
    private double _perfCompute, _perfDraw;
    private int _perfRebuilds;
    private long _perfLastLogMs;
    private long _chunkDiagLastMs;

    private void RebuildGraphics()
    {
        EnsureRoot();
        if (_rootRect == null || _rootChunk == null)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Layout stays global and runs every rebuild — it's cheap (a few ms) and keeps rects
        // correct across chunk boundaries without any cross-chunk coupling. - xlinka
        ComputeRects(Slot, null);
        ApplyScrollRects(Slot);
        _perfCompute += sw.Elapsed.TotalMilliseconds; sw.Restart();

        bool renderRoot = _fullDirty || _rootDirty;
        if (renderRoot)
        {
            _seenChunkRoots.Clear();
            _rootChunk.PrepareCompute();
            RenderPartition(_rootChunk.ContentRenderData, Slot, null, null);
            _rootChunk.SubmitChanges(0);

            // Render every nested chunk that's dirty/unbuilt. RenderChunk can discover deeper
            // nested roots, so drain a worklist until stable. - xlinka
            _chunkScratch.Clear();
            _chunkScratch.AddRange(_seenChunkRoots);
            for (int i = 0; i < _chunkScratch.Count; i++)
            {
                var root = _chunkScratch[i];
                bool built = _chunkBuilt.TryGetValue(root, out var b) && b;
                if (_fullDirty || _dirtyChunks.Contains(root) || !built)
                    RenderChunk(root);
                foreach (var r in _seenChunkRoots)
                    if (!_chunkScratch.Contains(r)) _chunkScratch.Add(r);
            }

            CleanupChunks();
        }
        else
        {
            // Only nested chunks changed (e.g. the sparkline): re-render just those, leaving the
            // root chunk's mesh (the whole file browser) resident and un-uploaded. - xlinka
            foreach (var root in _dirtyChunks)
            {
                if (_chunkMap.ContainsKey(root))
                    RenderChunk(root);
            }
        }

        _rootDirty = false;
        _fullDirty = false;
        _dirtyChunks.Clear();
        _perfDraw += sw.Elapsed.TotalMilliseconds;

        _perfRebuilds++;
        long nowMs = System.Environment.TickCount64;
        if (nowMs - _perfLastLogMs >= 1000)
        {
            int n = _perfRebuilds;
            Lumora.Core.Logging.Logger.Log(
                $"[CanvasPerf '{Slot?.SlotName.Value}'] {n} rebuilds/s | " +
                $"layout={_perfCompute:0.0} draw={_perfDraw:0.0} ms/s chunks={_chunkMap.Count} " +
                $"(avg/rebuild: {(_perfCompute + _perfDraw) / System.Math.Max(1, n):0.0}ms)");
            _perfCompute = _perfDraw = 0;
            _perfRebuilds = 0;
            _perfLastLogMs = nowMs;
        }
    }

    private void RenderChunk(GraphicChunkRoot root)
    {
        if (!_chunkMap.TryGetValue(root, out var chunk))
            return;

        var clip = ComputeInheritedClip(root.Slot);
        chunk.PrepareCompute();
        RenderPartition(chunk.ContentRenderData, root.Slot, clip, root);
        chunk.SubmitChanges(1);
        _chunkBuilt[root] = true;

        // TEMP: is the chunk producing geometry, and is its renderer alive / on top? - xlinka
        long nowMs = System.Environment.TickCount64;
        if (nowMs - _chunkDiagLastMs >= 1000)
        {
            _chunkDiagLastMs = nowMs;
            var mr = chunk.MeshRenderer;
            int matCount = mr?.Materials.Count ?? -1;
            int p0 = mr != null && matCount > 0 ? mr.GetSurfaceRenderPriority(0) : -999;
            int pLast = mr != null && matCount > 0 ? mr.GetSurfaceRenderPriority(matCount - 1) : -999;
            Lumora.Core.Logging.Logger.Log(
                $"[ChunkDiag '{root.Slot.SlotName.Value}'] verts={chunk.ContentRenderData.Mesh.VertexCount} " +
                $"onTop={chunk.RenderOnTop} mr={(mr != null ? mr.Enabled.Value.ToString() : "null")} " +
                $"chunkSlot={(chunk.ChunkSlot != null ? chunk.ChunkSlot.SlotName.Value : "null")} " +
                $"mats={matCount} prio[0]={p0} prio[last]={pLast}");
        }
    }

    private void EnsureChunk(GraphicChunkRoot root)
    {
        if (_chunkMap.ContainsKey(root))
            return;
        var rect = root.Slot.GetComponent<RectTransform>();
        if (rect == null)
            return;
        var chunk = new GraphicsChunk(this, rect);
        chunk.RenderOnTop = true;
        _chunkMap[root] = chunk;
        _chunkBuilt[root] = false;
    }

    private void CleanupChunks()
    {
        _chunkScratch.Clear();
        foreach (var pair in _chunkMap)
        {
            if (!_seenChunkRoots.Contains(pair.Key))
                _chunkScratch.Add(pair.Key);
        }
        foreach (var root in _chunkScratch)
        {
            _chunkMap[root].Dispose();
            _chunkMap.Remove(root);
            _chunkBuilt.Remove(root);
        }
    }

    // Nearest GraphicChunkRoot strictly below the canvas slot, or null if the rect belongs to the
    // root chunk. The canvas slot's own GraphicChunkRoot is the root marker, not a nested chunk. - xlinka
    private GraphicChunkRoot? FindChunkRoot(Slot? slot)
    {
        while (slot != null && !ReferenceEquals(slot, Slot))
        {
            var cr = slot.GetComponent<GraphicChunkRoot>();
            if (cr != null)
                return cr;
            slot = slot.Parent;
        }
        return null;
    }

    // Clip a nested chunk inherits from masks on its ancestors (above the chunk root). - xlinka
    private Rect? ComputeInheritedClip(Slot start)
    {
        Rect? clip = null;
        for (var s = start.Parent; s != null; s = s.Parent)
        {
            var mask = s.GetComponent<Mask>();
            var rect = s.GetComponent<RectTransform>();
            if (mask != null && mask.Enabled.Value && rect != null)
            {
                clip = clip.HasValue ? clip.Value.Intersection(rect.LocalComputeRect) : rect.LocalComputeRect;
            }
            if (ReferenceEquals(s, Slot))
                break;
        }
        return clip;
    }

    // bottom-up: anchor-rect every descendant, then apply this layout, which propagates
    // overrides into descendant subtrees via ReflowAfterParentChanged. - xlinka
    private void ComputeRects(Slot slot, RectTransform? parent)
    {
        if (slot != Slot && !slot.ActiveSelf.Value)
        {
            return;
        }

        var rect = slot.GetComponent<RectTransform>();
        var nextParent = parent;

        if (rect != null)
        {
            rect.SetRegisteredCanvas(this);
            rect.SetRectParent(parent);
            rect.ClearRectChildren();

            if (parent != null)
            {
                parent.AddRectChild(rect);
            }

            rect.SetLocalComputeRect(ComputeRect(rect, parent));
            nextParent = rect;
        }

        foreach (var child in slot.Children)
        {
            ComputeRects(child, nextParent);
        }
        foreach (var child in slot.LocalChildren)
        {
            ComputeRects(child, nextParent);
        }

        if (rect != null)
        {
            ApplyLayout(slot, rect);
        }
    }

    // run this slot's layout (if any) and reflow each child's subtree against the new rects. - xlinka
    private void ApplyLayout(Slot slot, RectTransform rect)
    {
        var layout = slot.GetComponent<LayoutController>();
        if (layout == null || !layout.Enabled.Value) return;

        layout.PrepareCompute();
        layout.ArrangeChildren(rect.RectChildren);

        foreach (var child in slot.Children) ReflowAfterParentChanged(child);
        foreach (var child in slot.LocalChildren) ReflowAfterParentChanged(child);
    }

    // a child's rect was just overridden by its parent's layout. re-anchor descendants and re-run inner layout. - xlinka
    private void ReflowAfterParentChanged(Slot slot)
    {
        var rect = slot.GetComponent<RectTransform>();
        if (rect != null)
        {
            foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
            foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
            ApplyLayout(slot, rect);
        }
        else
        {
            foreach (var c in slot.Children) ReflowAfterParentChanged(c);
            foreach (var c in slot.LocalChildren) ReflowAfterParentChanged(c);
        }
    }

    private void ReanchorAndDescend(Slot slot, RectTransform parent)
    {
        var rect = slot.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.SetLocalComputeRect(ComputeRect(rect, parent));
            foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
            foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
            ApplyLayout(slot, rect);
        }
        else
        {
            foreach (var c in slot.Children) ReanchorAndDescend(c, parent);
            foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, parent);
        }
    }

    private void ApplyScrollRects(Slot slot)
    {
        if (slot != Slot && !slot.ActiveSelf.Value)
        {
            return;
        }

        foreach (var scroll in slot.GetComponents<ScrollRect>())
        {
            if (!scroll.Enabled.Value)
            {
                continue;
            }

            if (scroll.ApplyScroll(out var content) && content != null)
            {
                ApplyLayout(content.Slot, content);
            }
        }

        foreach (var child in slot.Children)
        {
            ApplyScrollRects(child);
        }
        foreach (var child in slot.LocalChildren)
        {
            ApplyScrollRects(child);
        }
    }

    private static Rect ComputeRect(RectTransform rect, RectTransform? parent)
    {
        if (parent == null)
        {
            return Rect.FromMinMax(rect.OffsetMin.Value, rect.OffsetMax.Value);
        }

        var parentRect = parent.LocalComputeRect;
        var parentMin = parentRect.Min;
        var parentSize = parentRect.Size;
        var min = parentMin + parentSize * rect.AnchorMin.Value + rect.OffsetMin.Value;
        var max = parentMin + parentSize * rect.AnchorMax.Value + rect.OffsetMax.Value;
        return Rect.FromMinMax(min, max);
    }

    private bool TryRayToCanvasPoint(
        float3 rayOrigin,
        float3 rayDirection,
        out UIInteractionContext context,
        UIInteractionSource source,
        int pointerId,
        User? actor = null)
    {
        context = default;
        EnsureRoot();

        if (_rootRect == null || Slot == null || !Slot.IsActive)
        {
            return false;
        }

        float dirLength = rayDirection.Length;
        if (dirLength <= 0.000001f)
        {
            return false;
        }

        var normalizedDirection = rayDirection / dirLength;
        var localOrigin = Slot.GlobalPointToLocal(rayOrigin);
        var localDirection = Slot.GlobalDirectionToLocal(normalizedDirection);
        if (MathF.Abs(localDirection.z) <= 0.000001f)
        {
            return false;
        }

        float t = -localOrigin.z / localDirection.z;
        if (t < 0f)
        {
            return false;
        }

        var localPoint3 = localOrigin + localDirection * t;
        var localPoint = new float2(localPoint3.x, localPoint3.y);
        var worldPoint = Slot.LocalPointToGlobal(localPoint3);
        float distance = (worldPoint - rayOrigin).Length;

        context = new UIInteractionContext(
            this,
            source,
            pointerId,
            localPoint,
            worldPoint,
            rayOrigin,
            normalizedDirection,
            distance,
            actor);
        return true;
    }

    private void ScanHitSlot(Slot slot, in UIInteractionContext context, ref HitCandidate candidate, Rect? clipRect)
    {
        if (slot != Slot && !slot.ActiveSelf.Value)
        {
            return;
        }

        if (clipRect.HasValue && !clipRect.Value.Contains(context.LocalPoint))
        {
            return;
        }

        foreach (var component in slot.Components)
        {
            switch (component)
            {
                case InteractionBlock block when block.BlocksPoint(context.LocalPoint):
                    candidate = HitCandidate.Blocked;
                    break;
                case IUIInteractable interactable when interactable.CanInteract && interactable.IsPointInside(context.LocalPoint):
                    candidate = new HitCandidate(interactable);
                    break;
            }
        }

        var nextClip = clipRect;
        var mask = slot.GetComponent<Mask>();
        var rect = slot.GetComponent<RectTransform>();
        if (mask != null && mask.Enabled.Value && rect != null)
        {
            nextClip = nextClip.HasValue
                ? nextClip.Value.Intersection(rect.LocalComputeRect)
                : rect.LocalComputeRect;
            if (nextClip.Value.IsEmpty)
            {
                return;
            }
        }

        foreach (var child in slot.Children)
        {
            ScanHitSlot(child, in context, ref candidate, nextClip);
        }
        foreach (var child in slot.LocalChildren)
        {
            ScanHitSlot(child, in context, ref candidate, nextClip);
        }
    }

    private static int PointerKey(UIInteractionSource source, int pointerId)
    {
        return ((int)source << 24) ^ pointerId;
    }

    private bool DispatchAxis(IUIInteractable interactable, in UIInteractionContext context, in float2 axis)
    {
        foreach (var receiver in GetInteractionReceivers<IUIAxisActionReceiver>(interactable))
        {
            if (receiver.ProcessAxis(in context, in axis))
            {
                return true;
            }
        }

        return false;
    }

    private bool DispatchSecondary(IUIInteractable interactable, in UIInteractionContext context)
    {
        foreach (var receiver in GetInteractionReceivers<IUISecondaryActionReceiver>(interactable))
        {
            if (receiver.TriggerSecondary(in context))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<T> GetInteractionReceivers<T>(IUIInteractable interactable) where T : class
    {
        var directReceiver = interactable as T;
        if (directReceiver != null)
        {
            yield return directReceiver;
        }

        if (interactable is not Component component || component.Slot == null)
        {
            yield break;
        }

        var current = component.Slot;
        bool first = true;
        while (current != null)
        {
            if (!first && current.GetComponent<SearchBlock>() != null)
            {
                yield break;
            }

            foreach (var receiver in current.GetComponentsImplementing<T>())
            {
                if (ReferenceEquals(receiver, directReceiver))
                {
                    continue;
                }

                yield return receiver;
            }

            if (ReferenceEquals(current, Slot))
            {
                yield break;
            }

            first = false;
            current = current.Parent;
        }
    }

    // Render a slot subtree into one chunk's mesh, stopping at any nested GraphicChunkRoot (those
    // render into their own chunk). ownRoot is the chunk root we're rendering for (null = root). - xlinka
    private void RenderPartition(GraphicsChunk.RenderData rd, Slot slot, Rect? clipRect, GraphicChunkRoot? ownRoot)
    {
        if (slot != Slot && !slot.ActiveSelf.Value)
        {
            return;
        }

        if (clipRect.HasValue && clipRect.Value.IsEmpty)
        {
            return;
        }

        var mask = slot.GetComponent<Mask>();
        var rect = slot.GetComponent<RectTransform>();
        bool isMask = mask != null && mask.Enabled.Value && rect != null;
        bool showOwnGraphics = !isMask || mask!.ShowMaskGraphic.Value;

        mask?.PrepareCompute();

        if (showOwnGraphics)
        {
            foreach (var graphic in new List<Graphic>(slot.GetComponents<Graphic>()))
            {
                if (!graphic.Enabled.Value)
                {
                    continue;
                }

                graphic.PrepareCompute();
                if (graphic.RequiresPreGraphicsCompute)
                {
                    var preGraphics = graphic.PreGraphicsCompute();
                    if (!preGraphics.IsCompletedSuccessfully)
                    {
                        preGraphics.AsTask().GetAwaiter().GetResult();
                    }
                }

                rd.BeginGraphic();
                rd.SetClipRect(clipRect);
                graphic.ComputeGraphic(rd);
            }
        }

        var nextClip = clipRect;
        if (isMask)
        {
            nextClip = nextClip.HasValue
                ? nextClip.Value.Intersection(rect!.LocalComputeRect)
                : rect!.LocalComputeRect;
            if (nextClip.Value.IsEmpty)
            {
                return;
            }
        }

        foreach (var child in slot.Children)
        {
            RenderChildPartition(rd, child, nextClip, ownRoot);
        }
        foreach (var child in slot.LocalChildren)
        {
            RenderChildPartition(rd, child, nextClip, ownRoot);
        }
    }

    private void RenderChildPartition(GraphicsChunk.RenderData rd, Slot child, Rect? clip, GraphicChunkRoot? ownRoot)
    {
        var childRoot = child.GetComponent<GraphicChunkRoot>();
        if (childRoot != null && !ReferenceEquals(childRoot, ownRoot) && child.GetComponent<RectTransform>() != null)
        {
            // chunk boundary: register it and skip — it renders into its own mesh
            EnsureChunk(childRoot);
            _seenChunkRoots.Add(childRoot);
            return;
        }
        RenderPartition(rd, child, clip, ownRoot);
    }

    private static UIInteractionSource GetInteractionSource(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input != null && !input.IsVRActive)
        {
            return UIInteractionSource.Desktop;
        }

        return laser.ControllerSide.Value == Chirality.Left
            ? UIInteractionSource.VRLeft
            : UIInteractionSource.VRRight;
    }

    private static int GetPointerId(InteractionLaser laser)
    {
        return laser.ControllerSide.Value == Chirality.Left ? 1 : 2;
    }

    private static User? GetInteractionActor(InteractionLaser laser)
    {
        return laser?.Slot?.ActiveUser ?? laser?.World?.LocalUser;
    }

    private readonly struct HitCandidate
    {
        public static readonly HitCandidate Blocked = new HitCandidate(null);

        public readonly IUIInteractable? Interactable;

        public HitCandidate(IUIInteractable? interactable)
        {
            Interactable = interactable;
        }
    }
}
