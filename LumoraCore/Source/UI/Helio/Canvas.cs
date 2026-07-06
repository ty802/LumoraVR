
// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Assets;
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
        // Drag arbitration: where the press landed (canvas-local) and whether we've already handed the
        // press off to a scrolling ancestor this gesture.
        public float2 PressPoint;
        public bool Transferred;
    }

    private readonly Dictionary<int, PointerState> _pointers = new();
    private RectTransform? _rootRect;
    private GraphicChunkRoot? _chunkRoot;
    private GraphicsChunk? _rootChunk;
    // Optional physical collider sized to the aggregated UI bounds (grab/physics raycast against the canvas
    // surface). Attached only when SizeCollider is on; default off = purely additive. -xlinka
    private BoxCollider? _uiCollider;

    // Canvas-wide sorting boost fed into every chunk renderer's SortingOffset.
    // High values make the whole canvas draw in front of other transparent
    // geometry - overlay UI like the context menu uses this to render on top
    // of everything.
    public readonly Sync<int> SortingOrder = new();

    // Opt-in: size a BoxCollider on the canvas slot to the aggregated UI bounds, for physical/grab
    // interaction against the surface. Off by default - leaves hit-testing on the existing raycast path. -xlinka
    public readonly Sync<bool> SizeCollider = new();

    // Global kill-switch for GPU stencil masking (per-Mask opt-in via Mask.StencilMasking). On by default;
    // set false to force every mask back to the rectangular clip path. -xlinka
    public static bool StencilMaskingEnabled = true;

    // Scroll by translating the content chunk + counter-translating its clip, instead of mutating the content
    // rect every tick. Side-steps the rect-mutation never-settle freeze class entirely (it never touches the
    // rect). A ScrollRect's content gets its own GraphicChunkRoot and scrolls via GraphicChunkRoot.RenderOffset
    // (see ComputeChunk + ScrollRect.ApplyScroll). PARKED OFF: in-engine the content didn't visually move with
    // the flag on - the C# offset math and the ChunkSlot->Node3D->mesh transform chain both check out on
    // inspection, so it's a runtime propagation issue still under diagnosis. Falls back to the proven
    // rect-mutation scroll when off. -xlinka
    public static bool ScrollRenderOffset = true;

    // Per-chunk rendering: each GraphicChunkRoot below the canvas owns an independent mesh, so a
    // subtree that animates (e.g. the FPS sparkline) re-uploads only its own small mesh instead of
    // the whole canvas. The root chunk is everything not under a nested GraphicChunkRoot. - xlinka
    private readonly Dictionary<GraphicChunkRoot, GraphicsChunk> _chunkMap = new();
    private readonly Dictionary<GraphicChunkRoot, bool> _chunkBuilt = new();
    // Canvas-space rect each chunk was last MESHED at. Geometry is canvas-absolute, so a chunk whose
    // computed rect moved (a sibling above it changed size) has stale vertices and must re-mesh; one
    // whose rect is unchanged does NOT, even when a structural change elsewhere on the canvas fired.
    // This is what lets rebuilding ONE inspector pane leave the other pane (and its 60 row chunks)
    // untouched instead of re-tessellating the whole canvas (the "everything flashes" bug). -xlinka
    private readonly Dictionary<GraphicChunkRoot, Rect> _chunkMeshedRect = new();
    private readonly HashSet<GraphicChunkRoot> _dirtyChunks = new();
    // Chunks whose subtree layout (not just mesh) needs recomputing - scoped layout for
    // self-contained changes (e.g. a slider handle) so we don't re-lay-out the whole canvas.
    private readonly HashSet<GraphicChunkRoot> _layoutDirtyChunks = new();
    private readonly HashSet<GraphicChunkRoot> _seenChunkRoots = new();
    // Same chunks as _seenChunkRoots but kept in DISCOVERY (tree) order, so we can hand each chunk a
    // monotonically increasing SortingOrder in hierarchy order -> later-in-tree chunks tie-break on top. -xlinka
    private readonly List<GraphicChunkRoot> _chunkOrder = new();
    private readonly List<GraphicChunkRoot> _chunkScratch = new();
    // Companion set for the worklist: dedupe was List.Contains inside the drain loop = O(n^2) in
    // chunk count, which per-row chunking (70+ chunks per inspector panel) turned into real cost.
    private readonly HashSet<GraphicChunkRoot> _chunkScratchSet = new();

    // Canvas-wide shared materials: one text material per atlas / one property block per texture for
    // the WHOLE canvas. These lived per-chunk, which multiplied them by the chunk count once per-row
    // chunk roots landed. -xlinka
    private readonly Dictionary<TextureAsset, UITextMaterial> _sharedTextMaterials = new();
    private readonly Dictionary<IAssetProvider<TextureAsset>, MainTexturePropertyBlock> _sharedImageBlocks = new();
    private readonly Dictionary<IAssetProvider<TextureAsset>, UIUnlitMaterial> _sharedImageMaterials = new();

    // All text drawn from the same atlas shares one material -> one submesh per atlas (per clip). - xlinka
    internal UITextMaterial GetSharedTextMaterial(TextureAsset atlas)
    {
        if (!_sharedTextMaterials.TryGetValue(atlas, out var material) || material.IsDestroyed)
        {
            material = Slot.AddLocalSlot("TextMaterial").AttachComponent<UITextMaterial>();
            material.DirectTexture = atlas;
            // Reconstruct glyphs from the distance field when the atlas is MSDF; otherwise the shader falls back
            // to coverage. The atlas carries this so we don't need the FontAsset here. -xlinka
            material.UseMSDF.Value = atlas.IsMSDF;
            if (atlas.IsMSDF)
                material.PixelRange.Value = atlas.MsdfPixelRange;
            material.ForceUpdate();
            _sharedTextMaterials[atlas] = material;
        }
        return material;
    }

    // All images using the same texture share one property block -> one submesh per texture. - xlinka
    internal MainTexturePropertyBlock GetSharedImageBlock(IAssetProvider<TextureAsset> texture)
    {
        if (!_sharedImageBlocks.TryGetValue(texture, out var block) || block.IsDestroyed)
        {
            block = Slot.AddLocalSlot("ImageBlock").AttachComponent<MainTexturePropertyBlock>();
            block.Texture.Target = texture;
            _sharedImageBlocks[texture] = block;
        }
        return block;
    }

    // All images using the same texture share one material, with the texture carried ON the material
    // (like GetSharedTextMaterial) rather than a property block. The per-surface clone then samples the
    // texture directly, so live uniform writes - the scroll clip_offset - reach it. A property-block variant
    // is cloned once and cached, so it silently misses those writes and the image freezes while text scrolls. -xlinka
    internal UIUnlitMaterial GetSharedImageMaterial(IAssetProvider<TextureAsset> texture)
    {
        if (!_sharedImageMaterials.TryGetValue(texture, out var material) || material.IsDestroyed)
        {
            material = Slot.AddLocalSlot("ImageMaterial").AttachComponent<UIUnlitMaterial>();
            material.Texture.Target = texture;
            material.Culling.Value = Culling.None;
            material.ZWrite.Value = ZWrite.Off;
            material.RenderQueue.Value = 3000;
            material.ForceUpdate();
            _sharedImageMaterials[texture] = material;
        }
        return material;
    }
    // Nested chunks tessellated this rebuild, submitted after the compute pass. Split
    // so the compute (mesh build) can move to a worker while submit/upload stays on
    // the main thread (Godot mesh/material ops are main-thread only).
    private readonly List<GraphicChunkRoot> _computedChunks = new();
    private bool _rootDirty = true;
    private bool _fullDirty = true;
    private bool _layoutDirty = true;
    // Set after each mesh submit: the captured UI changed, so the offscreen viewport should render ONE frame
    // for it. Consumed by the host (dashboard). This is what lets the UI render on change instead of every
    // single frame. -xlinka
    private bool _renderRequested;
    // While rendering a render-offset scroll-content chunk, clip elision is disabled: an item fully inside
    // the viewport at bake time will scroll OUT later, and if its clip was elided it would spill past the
    // viewport. The chunk is baked once, so it must keep its clip for every item. -xlinka
    private bool _noClipElision;
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

    // Scoped visibility change: a free-anchored leaf (e.g. a checkbox dot, a hover overlay) inside a BUILT
    // chunk toggled active. Re-mesh just that chunk - it re-walks its subtree and picks up the new active
    // state - with NO full-canvas relayout, no root rebuild, and no chunk reconcile (which is what used to
    // disable a panel's nested row chunks and make the content vanish). Falls back to the safe full path if
    // the rect isn't inside a built chunk. Only the caller's layout-participation check decides which to use.
    // -xlinka
    public void MarkVisibilityDirty(RectTransform rect)
    {
        var root = FindChunkRoot(rect.Slot);
        // The scoped path re-meshes ONE built chunk and skips the render-root reconcile (CleanupChunks). That's
        // only safe when the toggled subtree lives entirely inside that one chunk. If it CONTAINS (or IS) a
        // nested chunk root - e.g. a whole dashboard screen whose rows are each their own chunk - those nested
        // chunks are independent and only the render-root path enables/disables them. Take the scoped path only
        // for pure leaf toggles (checkbox dot, hover overlay); otherwise a hidden screen's row chunks stay on
        // screen and overlap the new one. -xlinka
        if (root == null || !_chunkMap.ContainsKey(root) || !(_chunkBuilt.TryGetValue(root, out var built) && built)
            || SubtreeHasChunkRoot(rect.Slot))
        {
            _rootDirty = true;
            _layoutDirty = true;
            return;
        }
        _dirtyChunks.Add(root);
    }

    // True if this slot or anything in its subtree is a GraphicChunkRoot. Early-outs at the first hit, so it's
    // cheap for the leaf toggles that actually want the scoped path (a checkbox dot's subtree is a slot or two);
    // a screen returns true almost immediately off its own/first row's chunk root. -xlinka
    private bool SubtreeHasChunkRoot(Slot slot)
    {
        if (slot.GetComponent<GraphicChunkRoot>() != null)
            return true;
        var children = slot.Children;
        for (int i = 0; i < children.Count; i++)
            if (SubtreeHasChunkRoot(children[i]))
                return true;
        var localChildren = slot.LocalChildren;
        for (int i = 0; i < localChildren.Count; i++)
            if (SubtreeHasChunkRoot(localChildren[i]))
                return true;
        return false;
    }

    // A STRUCTURAL change (component attach/destroy/enable, i.e. a row added or removed): needs a
    // full layout recompute (it can resize this rect and reflow siblings) and a root pass (to discover
    // newly-added nested chunks), but NOT _fullDirty - that re-meshes every chunk on the canvas. The
    // changed chunk is marked dirty here; siblings that actually MOVED are caught by the moved-rect
    // check in the prepare loop, so only genuinely-affected chunks re-tessellate. -xlinka
    public void MarkStructuralDirty(RectTransform rect)
    {
        _layoutDirty = true;
        _rootDirty = true;
        var root = FindChunkRoot(rect.Slot);
        if (root != null && _chunkMap.ContainsKey(root))
            _dirtyChunks.Add(root);
    }

    // Has this chunk's canvas-space position/size changed since it was last meshed? Geometry is
    // canvas-absolute, so a moved chunk must re-mesh; an unmoved one can be reused. -xlinka
    private bool ChunkMoved(GraphicChunkRoot root)
    {
        var rt = root.Slot.GetComponent<RectTransform>();
        if (rt == null)
            return false;
        return !_chunkMeshedRect.TryGetValue(root, out var last) || !last.Equals(rt.LocalComputeRect);
    }

    // Scoped layout: a self-contained change (e.g. a slider handle anchor) inside a built chunk.
    // Re-lay-out only that chunk's subtree and re-mesh that chunk - no whole-canvas layout.
    public void MarkLayoutDirty(RectTransform rect)
    {
        var root = FindChunkRoot(rect.Slot);
        if (root == null || !_chunkMap.ContainsKey(root) || !(_chunkBuilt.TryGetValue(root, out var built) && built))
        {
            // Element lives in the root chunk (no owning nested chunk) or its chunk isn't built yet. Recompute
            // the whole layout (_layoutDirty) but only mark _rootDirty, NOT _fullDirty: the root chunk always
            // re-meshes on a root pass, and every nested chunk whose rect actually moved is caught by
            // ChunkMoved against the freshly recomputed rects. Setting _fullDirty here re-tessellated EVERY
            // chunk on any leaf move (a scrollbar handle nudging its own rect took the whole file grid down
            // with it). Nested-chunk elements never reach this branch - they take the scoped path below. -xlinka
            _layoutDirty = true;
            _rootDirty = true;
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

    /// <summary>
    /// True once after each rebuild/mesh submit. The host (dashboard) polls this each frame to render exactly
    /// ONE offscreen frame per change, instead of re-rendering the full-res capture every frame. -xlinka
    /// </summary>
    public bool ConsumeRenderRequested()
    {
        if (!_renderRequested)
            return false;
        _renderRequested = false;
        return true;
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
            state.PressPoint = context.LocalPoint;
            state.Transferred = false;
            state.Pressed?.NotifyPress(in context);
        }
        else if (isPressed && state.Pressed != null)
        {
            // Arbitration: a pressed child that opts into pass-through (a button/list item) relinquishes the
            // drag to the nearest scrolling ancestor once the pointer has moved past the threshold, so the
            // list scrolls instead of the child swallowing the gesture. A slider passes only cross-axis drags
            // and keeps its own. Done once per gesture. -xlinka
            if (!state.Transferred && state.Pressed is InteractionElement pressedElement)
            {
                var dragDelta = context.LocalPoint - state.PressPoint;
                if (pressedElement.PassDragToParent(in dragDelta))
                {
                    var scroll = FindScrollAncestor(pressedElement);
                    if (scroll != null)
                    {
                        pressedElement.NotifyRelease(in context); // give up the press, no submit
                        state.Pressed = scroll;
                        state.Transferred = true;
                        scroll.NotifyPress(in context);           // re-anchors the scroll at the current point
                    }
                }
            }
            state.Pressed?.NotifyDrag(in context);
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

    // Dispatch a wheel/axis to whatever pointer currently has a hover, regardless of source. The desktop
    // dashboard hovers the canvas under a VR* source (the laser is the pointer even on desktop), but the
    // raw OS mouse wheel is fed with no matching source - a fixed (Desktop,0) feed finds no hover and the
    // wheel does nothing. This routes it to the element actually under the cursor. -xlinka
    public bool ProcessAxisAnyPointer(in float2 axis)
    {
        if (axis == float2.Zero)
            return false;
        foreach (var state in _pointers.Values)
        {
            if (state.Hovered != null && DispatchAxis(state.Hovered, in state.LastContext, in axis))
                return true;
        }
        return false;
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

    // Nearest scrolling ancestor of a pressed element, for drag pass-through. ScrollRect is an
    // InteractionElement, so the press transfers to it cleanly (its OnPress re-anchors the scroll). -xlinka
    private static ScrollRect? FindScrollAncestor(InteractionElement from)
    {
        for (var slot = from.Slot?.Parent; slot != null; slot = slot.Parent)
        {
            var scroll = slot.GetComponent<ScrollRect>();
            if (scroll != null && scroll.CanInteract)
                return scroll;
        }
        return null;
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
        if (SizeCollider.Value)
            _uiCollider ??= Slot.GetComponent<BoxCollider>() ?? Slot.AttachComponent<BoxCollider>();
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
            // MEASURE (bottom-up) before ARRANGE (top-down): cache each rect's metrics so a parent's
            // arrange sees its children's fully-propagated min/preferred/flexible. -xlinka
            MeasureRects(Slot);
            ComputeRects(Slot, null);
            ApplyScrollRects(Slot);
            UpdateCanvasBounds();
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
            _chunkOrder.Clear();
            _rootChunk.PrepareCompute();
            RenderPartition(_rootChunk.ContentRenderData, Slot, null, null);

            // Prepare every nested chunk that's dirty/unbuilt. ComputeChunk can discover deeper
            // nested roots, so drain a worklist until stable. - xlinka
            _chunkScratch.Clear();
            _chunkScratchSet.Clear();
            foreach (var seen in _seenChunkRoots)
            {
                if (_chunkScratchSet.Add(seen))
                    _chunkScratch.Add(seen);
            }
            for (int i = 0; i < _chunkScratch.Count; i++)
            {
                var root = _chunkScratch[i];
                bool built = _chunkBuilt.TryGetValue(root, out var b) && b;
                // Re-mesh only chunks that are new, explicitly dirty, moved, or on a genuine full
                // refresh - NOT every chunk on any structural change. -xlinka
                if ((_fullDirty || _dirtyChunks.Contains(root) || !built || ChunkMoved(root)) && ComputeChunk(root))
                {
                    _computedChunks.Add(root);
                    if (root.Slot.GetComponent<RectTransform>() is { } crt)
                        _chunkMeshedRect[root] = crt.LocalComputeRect;
                }
                else
                    // Built + unchanged: we skip re-meshing it, but it may CONTAIN nested chunks (a panel chunk
                    // with its own per-row chunks). ComputeChunk would have discovered+registered those; since
                    // we skipped it, register them ourselves so they stay "seen" - otherwise CleanupChunks
                    // disables them and the panel's nested content vanishes when you toggle a control. -xlinka
                    RegisterNestedChunkRoots(root.Slot);
                foreach (var r in _seenChunkRoots)
                    if (_chunkScratchSet.Add(r)) _chunkScratch.Add(r);
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

        // Emit (geometry build) runs on a background worker. Everything
        // ComputeGraphic touches is snapshotted on the main thread in the prepare pass above
        // (PrepareCompute/PreGraphicsCompute) - graphic values, shaped glyph lines, font metrics,
        // cached texture sizes - so the worker only writes managed mesh data and never calls into
        // Godot. (The earlier "random disappear" was Text reading the Godot font server off-main;
        // those reads now happen on main.) A change arriving mid-cycle is gated by _updateRunning.
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

    // Per-surface unbounded ordering: each chunk renders every mesh surface as its own MeshInstance3D ordered by a
    // distinct full-range SortingOffset (= chunk tree index * stride + surface index) with uniform render_priority,
    // beating Godot's 256-level render_priority cap. DEFAULT OFF: it ghosts/double-renders during screen + modal
    // transitions (per-surface instances not torn down cleanly when a chunk is disabled/re-meshed - needs the
    // instance lifecycle tied to chunk visibility, debugged with the engine running). The banded path (off) is
    // stable. Only flip true for a focused render-test session, not for normal use. -xlinka
    public static bool UnboundedRenderOrder = false;

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
        // The captured mesh just changed on the main thread - ask the host for one offscreen render. -xlinka
        _renderRequested = true;
    }

    private void SubmitCycle()
    {
        if (_rootChunk == null)
            return;

        int baseOrder = SortingOrder.Value;

        // On a render-root cycle the chunk set / tree order can change, so (re)assign every seen chunk a
        // tree-order SortingOrder: root lowest, then nested chunks in discovery (hierarchy) order. SortingOrder
        // is the INNER transparent-sort key (Godot sorting_offset) under render_priority, so later-in-tree
        // chunks tie-break ON TOP of earlier same-band ones. Push it straight to each renderer here so unchanged
        // chunks reorder WITHOUT a re-mesh; recomputed/new chunks pick the same value up via SubmitChanges.
        // On a scoped cycle we don't rebuild _chunkOrder - each chunk keeps the OrderIndex it was last given,
        // which is still correct because the tree didn't change structurally. -xlinka
        if (_cycleRenderRoot)
        {
            _rootChunk.OrderIndex = baseOrder;
            for (int i = 0; i < _chunkOrder.Count; i++)
            {
                if (_chunkMap.TryGetValue(_chunkOrder[i], out var ch))
                {
                    ch.OrderIndex = baseOrder + 1 + i;
                    ch.ApplyOrderToRenderer();
                }
            }
            _rootChunk.SubmitChanges(_rootChunk.OrderIndex);
        }

        foreach (var root in _computedChunks)
        {
            if (_chunkMap.TryGetValue(root, out var chunk))
            {
                chunk.SubmitChanges(chunk.OrderIndex);
                // Render-offset content: its materials were just (re)cloned by submit, so re-pin the clip
                // offset onto them for the current scroll position. persist=true so the materials' own update
                // (queued by the re-clone) doesn't re-push the baked 0 and snap the content back to the top. -xlinka
                if (ScrollRenderOffset && root.ScrollContent)
                    chunk.SetClipOffset(root.RenderOffset, persist: true);
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
        var stencil = ComputeInheritedStencil(root.Slot);

        // Render-offset scrolling: the content is moved by the clip_offset uniform IN THE SHADER (it offsets
        // the vertex and clips against the same value, so position and clip can never desync), NOT by the
        // chunk transform. So the chunk slot stays at the origin; the scroll offset rides on the material's
        // clip_offset via SetClipOffset (after submit, and on each live scroll). The clip rect stays fixed in
        // canvas space, so it isn't in the material key and doesn't re-clone every step. -xlinka
        chunk.SetComputeOffset(float2.Zero);

        chunk.PrepareCompute();
        // Scroll content keeps every item's clip (no elision): items in view now scroll out later, and the
        // chunk is baked once, so an elided item would spill past the viewport once moved. -xlinka
        _noClipElision = ScrollRenderOffset && root.ScrollContent;
        // Scroll content must bake its FULL geometry (nothing culled or trimmed to the viewport), or items
        // below the fold are never in the mesh and scrolling reveals empty space. The shader still clips at
        // render via the material rect + clip_offset. -xlinka
        chunk.ContentRenderData.SuppressGeometryClip = _noClipElision;
        RenderPartition(chunk.ContentRenderData, root.Slot, clip, root, stencil);
        _noClipElision = false;
        return true;
    }

    // Scroll a ScrollRect's content by MOVING its chunk, not re-tessellating it - the whole point of the
    // perf fix. Sets the chunk slot's LocalPosition (the SlotHook flushes it to the
    // Node3D) and stores the offset so a later structural rebuild re-applies it. The mesh is untouched; the
    // ancestor stencil still clips the moved content. Returns false if the chunk isn't built yet, so the
    // caller can fall back to a full rebuild. -xlinka
    public bool ApplyScrollOffset(GraphicChunkRoot content, float2 pixelOffset)
    {
        if (content == null || !ScrollRenderOffset)
            return false;
        content.RenderOffset = pixelOffset;
        if (_chunkMap.TryGetValue(content, out var chunk) && chunk.ChunkSlot != null && !chunk.ChunkSlot.IsDestroyed)
        {
            // Move + clip the content in one shot via the shader's clip_offset uniform (no re-mesh, no chunk
            // transform, so position and clip stay locked together). persist=false: write the shader param
            // directly, no per-material asset rebuild - this is the hot path hit every scroll frame. -xlinka
            chunk.SetClipOffset(pixelOffset, persist: false);
            _renderRequested = true;   // repaint the offscreen dashboard capture this frame
            return true;
        }
        return false;
    }

    // Walk a chunk's subtree and register the FIRST GraphicChunkRoot on each branch into _seenChunkRoots,
    // WITHOUT re-meshing anything. Used when a render-root cycle skips recomputing a built+unchanged chunk:
    // its nested chunks still have to count as "seen" this cycle or CleanupChunks disables them. Mirrors
    // RenderPartition's active-self skipping; stops at each chunk boundary (the prepare loop then picks those
    // up and registers THEIR nested chunks in turn). -xlinka
    private void RegisterNestedChunkRoots(Slot slot)
    {
        var children = slot.Children;
        for (int i = 0; i < children.Count; i++)
            RegisterNestedChunkRootsRecursive(children[i]);
        var localChildren = slot.LocalChildren;
        for (int i = 0; i < localChildren.Count; i++)
            RegisterNestedChunkRootsRecursive(localChildren[i]);
    }

    private void RegisterNestedChunkRootsRecursive(Slot slot)
    {
        if (!slot.ActiveSelf.Value)
            return;
        var cr = slot.GetComponent<GraphicChunkRoot>();
        if (cr != null)
        {
            EnsureChunk(cr);
            if (_seenChunkRoots.Add(cr)) _chunkOrder.Add(cr);
            return; // chunk boundary
        }
        var children = slot.Children;
        for (int i = 0; i < children.Count; i++)
            RegisterNestedChunkRootsRecursive(children[i]);
        var localChildren = slot.LocalChildren;
        for (int i = 0; i < localChildren.Count; i++)
            RegisterNestedChunkRootsRecursive(localChildren[i]);
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
        chunk.OverlayLevel = root.OverlayLevel;
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
                _chunkMeshedRect.Remove(root);
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

    // Stencil role a nested chunk inherits from a stencil-enabled Mask on its ancestors (above the chunk
    // root). Mirror of ComputeInheritedClip - without this, a shaped mask over a separately-chunked subtree
    // (e.g. a scroll region or any GraphicChunkRoot) would silently fall back to the rect-AABB clip instead
    // of the stencil shape. Single-depth, so any ancestor stencil mask => Test (the writer lives in an
    // ancestor chunk, which draws first via the root/tree-order priority, so the stencil is set in time). -xlinka
    private StencilRole ComputeInheritedStencil(Slot start)
    {
        if (!StencilMaskingEnabled)
            return StencilRole.None;
        for (var s = start.Parent; s != null; s = s.Parent)
        {
            var mask = s.GetComponent<Mask>();
            if (mask != null && mask.Enabled.Value && mask.StencilMasking.Value && s.GetComponent<RectTransform>() != null)
            {
                return StencilRole.Test;
            }
            if (ReferenceEquals(s, Slot))
                break;
        }
        return StencilRole.None;
    }

    // bottom-up: anchor-rect every descendant, then apply this layout, which propagates
    // overrides into descendant subtrees via ReflowAfterParentChanged. - xlinka
    // MEASURE pass (bottom-up, post-order): cache each rect's LayoutMetrics so the top-down arrange sees a
    // child's fully-propagated min/preferred/flexible - flexibility now survives nested containers instead
    // of collapsing to 0. Gated by the same LayoutSubtreeDirty skip as ComputeRects, so clean subtrees keep
    // their cached metrics. Runs BEFORE ComputeRects, which is what clears LayoutSubtreeDirty. -xlinka
    private void MeasureRects(Slot slot)
    {
        if (slot != Slot && !slot.ActiveSelf.Value)
        {
            return;
        }

        var rect = slot.GetComponent<RectTransform>();
        // A clean, already-measured subtree keeps its cache (mirrors ComputeRects' cached-layout skip).
        if (rect != null && rect.MetricsValid && !rect.LayoutSubtreeDirty)
        {
            return;
        }

        // Children first, so a container measures over already-cached descendants.
        foreach (var child in slot.Children)
        {
            MeasureRects(child);
        }
        foreach (var child in slot.LocalChildren)
        {
            MeasureRects(child);
        }

        if (rect != null)
        {
            // GetMetrics runs each ILayoutElement's EnsureValidMetrics, which now reads the children's
            // cached measured metrics (available post-order) - no live recursion, no flexible decay.
            // PER-AXIS: re-measure only the axis whose cached metric is stale; reuse the clean one. A change
            // that couldn't be narrowed to one axis sets BOTH dirty bits (the common case), so both re-measure
            // and the output is identical to before. The win is a provably single-axis change skipping the
            // other axis's GetMetrics. -xlinka
            if (rect.MetricsDirty(LayoutDirection.Horizontal))
                rect.SetMeasuredMetrics(LayoutDirection.Horizontal, LayoutSizing.GetMetrics(rect, LayoutDirection.Horizontal));
            if (rect.MetricsDirty(LayoutDirection.Vertical))
                rect.SetMeasuredMetrics(LayoutDirection.Vertical, LayoutSizing.GetMetrics(rect, LayoutDirection.Vertical));
        }
    }

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
            if (parent != null)
            {
                parent.AddRectChild(rect);
            }

            var computed = ComputeRect(rect, parent);

            // CACHED LAYOUT: skip a clean subtree whose rect didn't change. Safe ONLY under a non-layout
            // parent - a LayoutController parent can reposition this rect from a sibling's change, so those
            // always recompute. A free-anchored rect's position depends solely on its anchors + the parent
            // rect, so if it's clean (nothing changed in it or below) and its recomputed rect matches the
            // cached one, the entire subtree is unchanged and its cached rects stay valid. This is what stops
            // a localized change (or a tab switch) from re-laying-out the whole canvas. -xlinka
            bool parentRunsLayout = parent != null && SlotRunsLayout(parent.Slot);
            if (!parentRunsLayout && !rect.LayoutSubtreeDirty && RectUnchanged(computed, rect.LocalComputeRect))
            {
                return;
            }

            rect.ClearRectChildren();
            rect.SetLocalComputeRect(computed);
            rect.ClearLayoutDirty();
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
            ApplyContentSizeFit(slot, rect);
            ApplyAspectRatio(slot, rect);
            ApplyLayout(slot, rect);
        }
    }

    // ContentSizeFitter / self-size: now that this container's children are registered, resize the
    // container's own rect to its content metric (about its pivot), then re-anchor the children against
    // the new size so a shrink-wrap panel/list/button fits its content instead of its anchor box. This
    // is the bottom-up sizing the layout system was missing - it only fires when a ContentSizeFitter is
    // present + enabled, so fixed-anchor layouts are unaffected. -xlinka
    private void ApplyContentSizeFit(Slot slot, RectTransform rect)
    {
        var fitter = slot.GetComponent<Helio.UI.Layout.ContentSizeFitter>();
        if (fitter == null || !fitter.Enabled.Value)
            return;

        var hFit = fitter.HorizontalFit.Value;
        var vFit = fitter.VerticalFit.Value;
        if (hFit == SizeFit.Disabled && vFit == SizeFit.Disabled)
            return;

        var r = rect.LocalComputeRect;
        var pivot = rect.Pivot.Value;
        float w = r.width;
        float h = r.height;

        if (hFit != SizeFit.Disabled)
        {
            var m = LayoutSizing.Measured(rect, LayoutDirection.Horizontal);
            w = hFit == SizeFit.MinSize ? m.Min : m.Preferred;
        }
        if (vFit != SizeFit.Disabled)
        {
            var m = LayoutSizing.Measured(rect, LayoutDirection.Vertical);
            h = vFit == SizeFit.MinSize ? m.Min : m.Preferred;
        }

        if (w == r.width && h == r.height)
            return;

        // Keep the pivot point fixed while the size changes.
        float pivotX = r.xMin + pivot.x * r.width;
        float pivotY = r.yMin + pivot.y * r.height;
        rect.SetLocalComputeRect(new Rect(pivotX - pivot.x * w, pivotY - pivot.y * h, w, h));

        foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
        foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
    }

    // Aspect-ratio lock: after content-fit, adjust this rect to a target width/height
    // ratio (about its pivot) and reflow children. Mirrors ApplyContentSizeFit's path.
    private void ApplyAspectRatio(Slot slot, RectTransform rect)
    {
        var constraint = slot.GetComponent<Helio.UI.Layout.AspectRatioConstraint>();
        if (constraint == null || !constraint.Enabled.Value)
            return;

        float aspect = constraint.AspectRatio.Value;
        if (aspect <= 0f)
            return;

        var r = rect.LocalComputeRect;
        float w = r.width;
        float h = r.height;

        switch (constraint.Mode.Value)
        {
            case Helio.UI.Layout.AspectRatioConstraint.AspectMode.WidthControlsHeight:
                h = w / aspect;
                break;
            case Helio.UI.Layout.AspectRatioConstraint.AspectMode.HeightControlsWidth:
                w = h * aspect;
                break;
            case Helio.UI.Layout.AspectRatioConstraint.AspectMode.FitInParent:
                if (w / aspect > h) w = h * aspect; else h = w / aspect;
                break;
            case Helio.UI.Layout.AspectRatioConstraint.AspectMode.EnvelopeParent:
                if (w / aspect < h) w = h * aspect; else h = w / aspect;
                break;
        }

        if (w == r.width && h == r.height)
            return;

        var pivot = rect.Pivot.Value;
        float pivotX = r.xMin + pivot.x * r.width;
        float pivotY = r.yMin + pivot.y * r.height;
        rect.SetLocalComputeRect(new Rect(pivotX - pivot.x * w, pivotY - pivot.y * h, w, h));

        foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
        foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
    }

    private static bool SlotRunsLayout(Slot? slot)
    {
        var layout = slot?.GetComponent<LayoutController>();
        return layout != null && layout.Enabled.Value;
    }

    private static bool RectUnchanged(in Rect a, in Rect b)
        => a.Min.x == b.Min.x && a.Min.y == b.Min.y && a.Size.x == b.Size.x && a.Size.y == b.Size.y;

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
            // Descend the subtree EXACTLY ONCE. If this slot runs a layout, ApplyLayout arranges its children
            // and reflows them - re-anchoring them first would just be overwritten. Only when there's no
            // layout do the children keep anchor-relative positions and need re-anchoring here. The old code
            // did BOTH at every level -> O(2^depth) relayout, which froze deep trees like the Session form. -xlinka
            if (SlotRunsLayout(slot))
            {
                ApplyLayout(slot, rect);
            }
            else
            {
                foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
                foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
            }
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
            // Descend once: a layout slot arranges + reflows its own children (ApplyLayout); a non-layout
            // slot re-anchors them. Doing both was the O(2^depth) blowup (see ReflowAfterParentChanged). -xlinka
            if (SlotRunsLayout(slot))
            {
                ApplyLayout(slot, rect);
            }
            else
            {
                foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
                foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
            }
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

        // Re-measure this subtree's metrics before re-arranging it (scoped path mirror of the full pump).
        MeasureRects(slot);

        rect.ClearRectChildren();
        foreach (var child in slot.Children)
            ComputeRects(child, rect);
        foreach (var child in slot.LocalChildren)
            ComputeRects(child, rect);
        ApplyContentSizeFit(slot, rect);
        ApplyAspectRatio(slot, rect);
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

    // Aggregate the active UI bounds (post-layout) and, when enabled, size the canvas collider to fit.
    // Runs once per layout-dirty cycle (after ComputeRects/ApplyScrollRects so the rects are final), NOT on
    // the hover/visual path. With SizeCollider off it only computes the root BoundingRect (available for a
    // cheap hit-test reject); it never touches scroll/layout output, so the scroll fix is unaffected. -xlinka
    private void UpdateCanvasBounds()
    {
        // Only walk the tree when something actually consumes the bounds (the collider). Bounds has no other
        // default consumer (the optional hit-test reject isn't wired), so running the full recursive walk on
        // every dashboard rebuild was pure cost - and it touches clean/cached subtrees the layout pass skips,
        // which is a place a stale child reference can bite. Gate it behind its consumer. -xlinka
        if (_rootRect == null || !SizeCollider.Value || _uiCollider == null) return;
        _rootRect.UpdateBounds();
        var b = _rootRect.BoundingRect;
        if (b.IsEmpty) return;
        // Flat slab in canvas-local space; tiny z keeps the Godot box shape from being degenerate.
        _uiCollider.Size.Value = new float3(b.width, b.height, 0.001f);
        _uiCollider.Offset.Value = new float3(b.Center.x, b.Center.y, 0f);
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

        // Index iteration (not foreach) so this per-frame, per-slot scan doesn't allocate an enumerator on
        // the IReadOnlyList each call. -xlinka
        var comps = slot.Components;
        for (int ci = 0; ci < comps.Count; ci++)
        {
            switch (comps[ci])
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

        var children = slot.Children;
        for (int i = 0; i < children.Count; i++)
        {
            ScanHitSlot(children[i], in context, ref candidate, nextClip);
        }
        var localChildren = slot.LocalChildren;
        for (int i = 0; i < localChildren.Count; i++)
        {
            ScanHitSlot(localChildren[i], in context, ref candidate, nextClip);
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
    private void RenderPartition(GraphicsChunk.RenderData rd, Slot slot, Rect? clipRect, GraphicChunkRoot? ownRoot, StencilRole stencil = StencilRole.None)
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
        bool stencilMask = isMask && mask!.StencilMasking.Value && StencilMaskingEnabled;
        bool showOwnGraphics = !isMask || mask!.ShowMaskGraphic.Value;

        mask?.PrepareCompute();

        if (stencilMask)
        {
            // A stencil mask stamps its SHAPE into the stencil buffer (Write pass). Queued BEFORE the
            // children recurse, and render_priority follows submission order, so the writer draws first and
            // the tested content draws after - even though the mask itself is invisible. -xlinka
            EmitGraphics(rd, slot, clipRect, StencilRole.Write);
        }
        else if (showOwnGraphics)
        {
            EmitGraphics(rd, slot, clipRect, stencil);
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

        // Content under a stencil mask is stencil-TESTED; otherwise it inherits our role.
        var nextStencil = stencilMask ? StencilRole.Test : stencil;

        foreach (var child in slot.Children)
        {
            RenderChildPartition(rd, child, nextClip, ownRoot, nextStencil);
        }
        foreach (var child in slot.LocalChildren)
        {
            RenderChildPartition(rd, child, nextClip, ownRoot, nextStencil);
        }
    }

    // Queue a slot's own enabled graphics for the worker emit pass, tagging each with its stencil role. -xlinka
    private void EmitGraphics(GraphicsChunk.RenderData rd, Slot slot, Rect? clipRect, StencilRole stencil)
    {
        // Indexed scan: GetComponents allocated a LINQ iterator + a copy list PER SLOT PER REBUILD.
        var comps = slot.Components;
        for (int ci = 0; ci < comps.Count; ci++)
        {
            if (comps[ci] is not Graphic graphic || !graphic.Enabled.Value)
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

            // Clip elision: if the inherited clip fully encloses this graphic's bounds it discards nothing,
            // so drop it - the graphic then batches into the shared unclipped submesh instead of spawning a
            // per-clip material clone + an extra Godot surface. Only sound for quad graphics bounded by their
            // rect (Image family), never text (glyphs can overflow the rect), never a stencil graphic (its
            // key must keep the shape). Common win: scroll items fully in view, content well inside a panel
            // mask. -xlinka
            var effectiveClip = clipRect;
            if (effectiveClip.HasValue && !_noClipElision && stencil == StencilRole.None && IsRectBoundedGraphic(graphic))
            {
                var grect = slot.GetComponent<RectTransform>();
                if (grect != null && effectiveClip.Value.Encloses(grect.LocalComputeRect))
                {
                    effectiveClip = null;
                }
            }

            rd.QueueGraphic(graphic, effectiveClip, stencil);
        }
    }

    // Graphics whose geometry never exceeds their RectTransform rect, so an enclosing clip is a no-op for
    // them. Text is excluded - glyph ascenders/descenders can overflow the layout rect. -xlinka
    private static bool IsRectBoundedGraphic(Graphic graphic)
        => graphic is Image or RawImage or TiledRawImage or BorderedImage;

    private void RenderChildPartition(GraphicsChunk.RenderData rd, Slot child, Rect? clip, GraphicChunkRoot? ownRoot, StencilRole stencil = StencilRole.None)
    {
        // An inactive child renders nothing - and, critically, must NOT register its chunk root as "seen"
        // this cycle. If it did, CleanupChunks wouldn't disable that chunk, and a hidden overlay/screen
        // (a canvas-root modal like the create-world dialog) keeps drawing on top of the next screen -
        // the "two screens at once" overlap. RenderPartition and RegisterNestedChunkRoots already skip
        // inactive slots before touching their chunk roots; this discovery path was the one that didn't. -xlinka
        if (!child.ActiveSelf.Value)
            return;

        var childRoot = child.GetComponent<GraphicChunkRoot>();
        if (childRoot != null && !ReferenceEquals(childRoot, ownRoot) && child.GetComponent<RectTransform>() != null)
        {
            // chunk boundary: register it and skip - it renders into its own mesh
            EnsureChunk(childRoot);
            if (_seenChunkRoots.Add(childRoot)) _chunkOrder.Add(childRoot);
            return;
        }
        RenderPartition(rd, child, clip, ownRoot, stencil);
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
