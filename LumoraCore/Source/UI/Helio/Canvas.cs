
// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    // Canvas-wide sorting boost fed into every chunk renderer's SortingOffset.
    // High values make the whole canvas draw in front of other transparent
    // geometry - overlay UI like the context menu uses this to render on top
    // of everything.
    public readonly Sync<int> SortingOrder = new();

    // Per-chunk rendering: each GraphicChunkRoot below the canvas owns an independent mesh, so a
    // subtree that animates (e.g. the FPS sparkline) re-uploads only its own small mesh instead of
    // the whole canvas. The root chunk is everything not under a nested GraphicChunkRoot. - xlinka
    private readonly Dictionary<GraphicChunkRoot, GraphicsChunk> _chunkMap = new();
    private readonly Dictionary<GraphicChunkRoot, bool> _chunkBuilt = new();
    private readonly HashSet<GraphicChunkRoot> _dirtyChunks = new();
    // Chunks whose subtree layout (not just mesh) needs recomputing - scoped layout for
    // self-contained changes (e.g. a slider handle) so we don't re-lay-out the whole canvas.
    private readonly HashSet<GraphicChunkRoot> _layoutDirtyChunks = new();
    private readonly HashSet<GraphicChunkRoot> _seenChunkRoots = new();
    private readonly List<GraphicChunkRoot> _chunkScratch = new();
    // Nested chunks tessellated this rebuild, submitted after the compute pass. Split
    // so the compute (mesh build) can move to a worker while submit/upload stays on
    // the main thread (Godot mesh/material ops are main-thread only).
    private readonly List<GraphicChunkRoot> _computedChunks = new();
    private bool _rootDirty = true;
    private bool _fullDirty = true;
    private bool _layoutDirty = true;
    // Guards the async rebuild cycle: prepare (main) -> emit (worker) -> submit (main). Only one
    // cycle at a time; a change mid-cycle leaves its dirty flag set so the next OnCommonUpdate
    // re-runs. Touched only on the main thread (OnCommonUpdate + the RunSynchronously submit
    // callback), so no locking is needed.
    private bool _updateRunning;
    private bool _cycleRenderRoot;

    // Full refresh: recompute layout and rebuild the root chunk and every nested chunk. - xlinka
    public void MarkDirty()
    {
        _fullDirty = true;
        _layoutDirty = true;
    }

    // A layout-affecting change happened (rect/anchor/offset, layout component, structure,
    // scroll). Recompute rects and re-render everything, since moved chunks aren't individually
    // tracked. Pure visual changes go through MarkDirty(rect) instead, which re-meshes only the
    // touched chunk and reuses cached rects - that's what keeps hover/press off the layout path.
    public void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _fullDirty = true;
    }

    // A slot's active state changed (content shown/hidden) - NOT a content change. Runs the chunk
    // reconcile (re-enable newly-visible persisted chunks, disable hidden ones) + a layout pass,
    // but deliberately does NOT set _fullDirty: built chunks just toggle back on (instant, no
    // re-tessellate, no worker hop), and only genuinely new/changed content re-meshes. This is what
    // makes re-showing content appear immediately instead of popping in a frame late.
    public void MarkVisibilityDirty()
    {
        _rootDirty = true;
        _layoutDirty = true;
    }

    // Scoped layout: a self-contained change (e.g. a slider handle anchor) inside a built chunk.
    // Re-lay-out only that chunk's subtree and re-mesh that chunk - no whole-canvas layout. Falls
    // back to a full layout if the rect isn't inside a built chunk (the change may affect siblings).
    public void MarkLayoutDirty(RectTransform rect)
    {
        var root = FindChunkRoot(rect.Slot);
        if (root == null || !_chunkMap.ContainsKey(root) || !(_chunkBuilt.TryGetValue(root, out var built) && built))
        {
            _layoutDirty = true;
            _fullDirty = true;
            return;
        }
        _layoutDirtyChunks.Add(root);
        _dirtyChunks.Add(root);
    }

    // Route a change to the chunk that owns it, so only that chunk re-renders/uploads.
    // A chunk that hasn't been built yet (not in _chunkMap) can't be re-rendered by
    // the per-chunk path - it's only discovered/built during a root pass. So escalate
    // to a root rebuild, otherwise its content stays invisible until some unrelated
    // root-dirty event (e.g. a hover) finally builds it. - xlinka
    public void MarkDirty(RectTransform rect)
    {
        var root = FindChunkRoot(rect.Slot);
        if (root == null || !_chunkMap.ContainsKey(root))
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
        bool dirty = _rootDirty || _fullDirty || _layoutDirty || _dirtyChunks.Count > 0 || _layoutDirtyChunks.Count > 0;
        if (!dirty)
            return;
        // One rebuild cycle at a time. A change arriving mid-cycle leaves its dirty flag set (we
        // don't clear it here), so the next OnCommonUpdate after the cycle finishes re-triggers.
        if (_updateRunning)
            return;
        StartRebuildCycle();
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

    // MAIN: layout + prepare each dirty chunk (snapshot graphics, rasterize glyphs, queue geometry,
    // discover nested chunks), then hand the geometry build to a worker and submit back on main.
    private void StartRebuildCycle()
    {
        EnsureRoot();
        if (_rootRect == null || _rootChunk == null)
        {
            return;
        }

        // Only recompute the full layout when something layout-affecting actually changed.
        // Pure visual changes (hover/press tints, etc.) come in via MarkDirty(rect) without
        // setting _layoutDirty, so they re-mesh just their chunk and reuse the cached rects -
        // this is what stops every hover from re-laying-out the whole canvas. - xlinka
        if (_layoutDirty)
        {
            ComputeRects(Slot, null);
            ApplyScrollRects(Slot);
            _layoutDirty = false;
            _layoutDirtyChunks.Clear();
        }
        else if (_layoutDirtyChunks.Count > 0)
        {
            // Scoped: only the touched chunks' subtrees moved (e.g. a slider handle). Re-lay-out
            // those subtrees against their unchanged chunk-root rect; their chunk is already in
            // _dirtyChunks so the mesh re-renders too. No whole-canvas layout pass.
            foreach (var root in _layoutDirtyChunks)
                RelayoutChunkSubtree(root);
            _layoutDirtyChunks.Clear();
        }

        bool renderRoot = _fullDirty || _rootDirty;
        _cycleRenderRoot = renderRoot;

        // PREPARE PASS (main): clear meshes + queue graphics (snapshot + glyph raster on main),
        // discover nested chunks. The geometry build (ComputeGraphic) is queued, not run here.
        _computedChunks.Clear();
        if (renderRoot)
        {
            _seenChunkRoots.Clear();
            _rootChunk.PrepareCompute();
            RenderPartition(_rootChunk.ContentRenderData, Slot, null, null);

            // Prepare every nested chunk that's dirty/unbuilt. ComputeChunk can discover deeper
            // nested roots, so drain a worklist until stable. - xlinka
            _chunkScratch.Clear();
            _chunkScratch.AddRange(_seenChunkRoots);
            for (int i = 0; i < _chunkScratch.Count; i++)
            {
                var root = _chunkScratch[i];
                bool built = _chunkBuilt.TryGetValue(root, out var b) && b;
                if ((_fullDirty || _dirtyChunks.Contains(root) || !built) && ComputeChunk(root))
                    _computedChunks.Add(root);
                foreach (var r in _seenChunkRoots)
                    if (!_chunkScratch.Contains(r)) _chunkScratch.Add(r);
            }

            // Re-enable every chunk visible this cycle. Persisted chunks that weren't recomputed
            // (built + unchanged) just toggle their slot back on here - instant, no re-tessellate;
            // recomputed ones get their fresh mesh at submit. This is what makes re-showing content
            // appear immediately instead of rebuilding from scratch.
            foreach (var seen in _seenChunkRoots)
                if (_chunkMap.TryGetValue(seen, out var chunk))
                    chunk.SetActive(true);
        }
        else
        {
            // Only nested chunks changed (e.g. the sparkline): re-mesh just those.
            foreach (var root in _dirtyChunks)
            {
                if (_chunkMap.ContainsKey(root) && ComputeChunk(root))
                    _computedChunks.Add(root);
            }
        }

        // Dirty handled this cycle; a change arriving during the worker/submit sets its own flag again.
        _rootDirty = false;
        _fullDirty = false;
        _dirtyChunks.Clear();

        _updateRunning = true;

        // ISOLATION (2026-06-15): the worker path is temporarily disabled while diagnosing a
        // blank-content regression. Emit + submit run inline on the main thread here, so this is
        // functionally the synchronous Step-1/2 build (datamodel-pure compute, deferred materials),
        // just without the Task.Run hop. Flip USE_WORKER back on once content renders correctly.
        if (UseWorker)
        {
            Task.Run(() =>
            {
                try
                {
                    EmitCycle();
                }
                catch (Exception ex)
                {
                    Lumora.Core.Logging.Logger.Error($"Canvas graphics worker failed: {ex}");
                }
                finally
                {
                    World?.RunSynchronously(FinishRebuildCycle);
                }
            });
        }
        else
        {
            EmitCycle();
            FinishRebuildCycle();
        }
    }

    // Master switch for the off-main-thread emit. static readonly (not const) so both branches
    // compile without an unreachable warning when toggled.
    private static readonly bool UseWorker = true;

    // WORKER: drain each dirty chunk's emit queue into its (already-cleared) mesh. Reads only
    // snapshotted graphic state + stable LocalComputeRect; writes only managed PhosMesh data.
    private void EmitCycle()
    {
        if (IsDestroyed)
            return;
        if (_cycleRenderRoot)
            _rootChunk?.ContentRenderData.EmitQueued();
        foreach (var root in _computedChunks)
            if (_chunkMap.TryGetValue(root, out var chunk))
                chunk.ContentRenderData.EmitQueued();
    }

    // MAIN: push the worker-built meshes + their materials to the renderers, then release the guard.
    private void FinishRebuildCycle()
    {
        if (!IsDestroyed)
            SubmitCycle();
        // A change that arrived during the cycle left its dirty flag set, so the next
        // OnCommonUpdate starts a fresh cycle.
        _updateRunning = false;
    }

    private void SubmitCycle()
    {
        if (_rootChunk == null)
            return;
        if (_cycleRenderRoot)
            _rootChunk.SubmitChanges(SortingOrder.Value);
        foreach (var root in _computedChunks)
        {
            if (_chunkMap.TryGetValue(root, out var chunk))
            {
                chunk.SubmitChanges(SortingOrder.Value + 1);
                _chunkBuilt[root] = true;
            }
        }
        if (_cycleRenderRoot)
            CleanupChunks();
    }

    // Tessellate a nested chunk's mesh without submitting it. Returns true if computed.
    private bool ComputeChunk(GraphicChunkRoot root)
    {
        if (!_chunkMap.TryGetValue(root, out var chunk))
            return false;

        var clip = ComputeInheritedClip(root.Slot);
        chunk.PrepareCompute();
        RenderPartition(chunk.ContentRenderData, root.Slot, clip, root);
        return true;
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

    // Reconcile chunks not seen this cycle. A chunk whose root slot is gone is truly removed -> dispose.
    // A chunk that's just hidden (subtree inactive) is DISABLED but kept, so re-showing it is an
    // instant re-enable, not a dispose + rebuild. - xlinka
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
            var chunk = _chunkMap[root];
            bool removed = root.IsDestroyed || root.Slot == null || root.Slot.IsDestroyed;
            if (removed)
            {
                chunk.Dispose();
                _chunkMap.Remove(root);
                _chunkBuilt.Remove(root);
            }
            else
            {
                chunk.SetActive(false);
            }
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

    // Re-lay-out the subtree under a chunk root, keeping the chunk root's own rect (its parent
    // didn't change, so its rect is unchanged). Mirrors ComputeRects for the children only, so the
    // parent's child list is untouched - used for scoped layout of self-contained changes.
    private void RelayoutChunkSubtree(GraphicChunkRoot root)
    {
        var slot = root.Slot;
        if (slot != Slot && !slot.ActiveSelf.Value)
            return;
        var rect = slot.GetComponent<RectTransform>();
        if (rect == null)
            return;

        rect.ClearRectChildren();
        foreach (var child in slot.Children)
            ComputeRects(child, rect);
        foreach (var child in slot.LocalChildren)
            ComputeRects(child, rect);
        ApplyLayout(slot, rect);
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

                // MAIN: snapshot + glyph rasterization stay here (Godot-touching). The geometry
                // build (ComputeGraphic) is deferred to the worker via the chunk's emit queue.
                graphic.PrepareCompute();
                if (graphic.RequiresPreGraphicsCompute)
                {
                    var preGraphics = graphic.PreGraphicsCompute();
                    if (!preGraphics.IsCompletedSuccessfully)
                    {
                        preGraphics.AsTask().GetAwaiter().GetResult();
                    }
                }

                rd.QueueGraphic(graphic, clipRect);
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
            // chunk boundary: register it and skip - it renders into its own mesh
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
