// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Logging;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

public sealed class GraphicsChunk
{
    public sealed class RenderData
    {
        private const int DefaultLogicalRenderQueue = 3000;
        private const int GodotRenderPriorityMin = -128;
        private const int GodotRenderPriorityMax = 127;

        private readonly Dictionary<MaterialKey, List<PhosTriangleSubmesh>> _requestedMaterials = new();
        private readonly Dictionary<MaterialKey, AssignedMaterial> _assignedMaterials = new();
        // Captured on the main thread (prepare walk), drained on the worker (EmitQueued). The worker
        // never traverses the live slot tree - it only iterates this queue and calls ComputeGraphic,
        // which reads each graphic's already-snapshotted state + stable LocalComputeRect. - xlinka
        private readonly List<(Graphic Graphic, Rect? Clip, StencilRole Stencil)> _emitQueue = new();
        private readonly GraphicsChunk _chunk;
        private int _minimumSubmeshIndex;
        private Rect? _clipRect;
        private StencilRole _stencilRole;
        // Latch so the render-priority band-exhaustion warning fires once per RenderData, not every submit. -xlinka
        private bool _loggedBandExhaustion;

        public PhosMesh Mesh { get; } = new();
        public Rect? ClipRect => _clipRect;

        // Scroll content is baked ONCE and moved by the clip_offset uniform, so items off-screen at bake time
        // still have to be in the mesh (they scroll into view later) and on-screen items must NOT be trimmed to
        // the viewport (the shader clips them at render). When true, graphics skip geometry culling/trimming and
        // bake their full quads; the clip rect still rides on the material (GetSubmesh uses _clipRect) so the
        // shader clips correctly. -xlinka
        public bool SuppressGeometryClip { get; set; }

        // Clip rect for GEOMETRY culling/trimming - null when SuppressGeometryClip (bake full). The MATERIAL clip
        // (shader-side, via GetSubmesh) always uses the real _clipRect regardless. -xlinka
        public Rect? GeometryClipRect => SuppressGeometryClip ? null : _clipRect;

        public RenderData(GraphicsChunk chunk)
        {
            _chunk = chunk;
            PrepareMesh();
        }

        public IReadOnlyDictionary<MaterialKey, AssignedMaterial> AssignedMaterials => _assignedMaterials;

        public void SetClipRect(Rect? clipRect)
        {
            _clipRect = clipRect;
        }

        public void PrepareCompute()
        {
            Mesh.Clear();
            _requestedMaterials.Clear();
            _emitQueue.Clear();
            _minimumSubmeshIndex = 0;
            PrepareMesh();
        }

        public void BeginGraphic()
        {
            _minimumSubmeshIndex = Mesh.Submeshes.Count;
        }

        // MAIN: queue a prepared graphic (with its computed clip + stencil role) for the worker emit pass.
        public void QueueGraphic(Graphic graphic, Rect? clip, StencilRole stencil = StencilRole.None)
        {
            _emitQueue.Add((graphic, clip, stencil));
        }

        // WORKER: build the geometry for every queued graphic. No slot/datamodel access (materials
        // were keyed by identity and are resolved at submit), no tree traversal - just each graphic's
        // ComputeGraphic reading its snapshot + stable LocalComputeRect into this chunk's mesh.
        public void EmitQueued()
        {
            for (int i = 0; i < _emitQueue.Count; i++)
            {
                var (graphic, clip, stencil) = _emitQueue[i];
                BeginGraphic();
                SetClipRect(clip);
                _stencilRole = stencil;
                graphic.ComputeGraphic(this);
            }
        }

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material)
            => GetSubmesh(new MaterialKey(material, null, null, _clipRect, _stencilRole));

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material, MaterialMapper mapper)
            => GetSubmesh(new MaterialKey(material, null, mapper, _clipRect, _stencilRole));

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material, object? key, MaterialMapper? mapper)
            => GetSubmesh(new MaterialKey(material, key, mapper, _clipRect, _stencilRole));

        public PhosTriangleSubmesh GetSubmesh(in MaterialKey key)
        {
            if (!_requestedMaterials.TryGetValue(key, out var submeshes))
            {
                submeshes = new List<PhosTriangleSubmesh>();
                _requestedMaterials.Add(key, submeshes);
            }

            foreach (var existing in submeshes)
            {
                if (existing.Index >= _minimumSubmeshIndex)
                {
                    return existing;
                }
            }

            var submesh = new PhosTriangleSubmesh(Mesh);
            Mesh.Submeshes.Add(submesh);
            submeshes.Add(submesh);
            return submesh;
        }

        public T AttachPropertyBlock<T>() where T : Component, IAssetProvider<MaterialPropertyBlockAsset>, new()
        {
            _chunk.SetupComponents();
            return _chunk.ChunkSlot.AddLocalSlot("PropertyBlock").AttachComponent<T>();
        }

        // Deferred material resolution. The compute/emit pass keys submeshes by texture/atlas
        // IDENTITY (no slot creation), and these run at SUBMIT time on the main thread to create
        // the shared material + property-block slots. Keeping slot creation out of the emit pass
        // is what lets that pass run off the main thread. Static (no capture) so the delegate
        // reference is stable and submeshes with the same texture/atlas still batch together.
        public static readonly MaterialMapper ImageTexture =
            (rd, baseMaterial, key, usingDefault) =>
            {
                if (key is not IAssetProvider<TextureAsset> texture)
                {
                    return new MaterialMap(baseMaterial);
                }

                // Default-material image: carry the texture on a shared per-texture material so the per-surface
                // clone samples it directly and live uniform writes (the scroll clip_offset) reach it. A custom
                // material keeps the property-block path - its variant is cached, so it won't scroll-clip, but
                // custom-material images aren't the scrollable list case. -xlinka
                return usingDefault
                    ? new MaterialMap(rd.GetSharedImageMaterial(texture))
                    : new MaterialMap(baseMaterial, rd.GetSharedImageBlock(texture));
            };

        public static readonly MaterialMapper TextAtlas =
            (rd, baseMaterial, key, usingDefault) =>
                key is TextureAsset atlas ? new MaterialMap(rd.GetSharedTextMaterial(atlas)) : new MaterialMap(baseMaterial);

        public UITextMaterial GetSharedTextMaterial(TextureAsset atlas) => _chunk.GetSharedTextMaterial(atlas);

        public MainTexturePropertyBlock GetSharedImageBlock(IAssetProvider<TextureAsset> texture) => _chunk.GetSharedImageBlock(texture);

        public UIUnlitMaterial GetSharedImageMaterial(IAssetProvider<TextureAsset> texture) => _chunk.GetSharedImageMaterial(texture);

        internal void SubmitChanges(int sortingOrder)
        {
            _chunk.SetupComponents();
            EnsureComponents();
            AssignMaterials();

            _chunk.MeshRenderer!.SortingOrder.Value = sortingOrder;
            // Opt-in unbounded ordering: tell the hook to split this chunk's surfaces into per-surface
            // instances ordered by SortingOffset instead of the render_priority bands. Default false. -xlinka
            _chunk.MeshRenderer.PerSurfaceOrdering = Canvas.UnboundedRenderOrder;
            _chunk.MeshRenderer.Enabled.Value = Mesh.VertexCount > 0;
            _chunk.MeshProvider!.SetMesh(Mesh);

            // MeshHook will ClearSurfaces + AddSurfaceFromArrays the underlying ArrayMesh, which
            // wipes Godot's surface override materials. The MeshRenderer's own SyncRef sees no
            // change, so we have to flag it ourselves or every surface past frame 0 renders bare. - xlinka
            _chunk.MeshRenderer.FlagSurfacesDirty();
        }

        private void AssignMaterials()
        {
            var requests = BuildMaterialRequests(out var submeshQueues);
            SortSubmeshesByRenderQueue(submeshQueues);

            var surfaceIndexes = BuildSurfaceIndexes();
            int materialCount = surfaceIndexes.Count;
            _chunk.MeshRenderer!.Materials.EnsureExactCount(materialCount);
            _chunk.MeshRenderer.MaterialPropertyBlocks.EnsureExactCount(materialCount);
            _chunk.MeshRenderer.EnsureExactSurfaceRenderPriorityCount(materialCount);

            // Each surface needs its OWN Godot render_priority based on submission
            // order (tree depth-first, after SortSubmeshesByRenderQueue), AND its own
            // cloned material - MeshRendererHook applies priority by mutating
            // Material.RenderPriority in place, so if surfaces shared a material
            // their priorities would collapse to whichever value was written last.
            // Coplanar UI quads then fall back to Godot's distance sort, which
            // flips with camera angle and produces the "wash" artifact. - xlinka
            var perSurfacePriorities = BuildPerSurfacePriorities(materialCount, _chunk.RenderOnTop, _chunk.OverlayLevel);
            var activeMaterials = new HashSet<MaterialKey>();
            foreach (var request in requests)
            {
                var indexes = new List<int>(request.Submeshes.Count);
                foreach (var submesh in request.Submeshes)
                {
                    if (surfaceIndexes.TryGetValue(submesh, out int surfaceIndex))
                    {
                        indexes.Add(surfaceIndex);
                    }
                }

                if (indexes.Count == 0)
                {
                    continue;
                }

                activeMaterials.Add(request.Key);
                var singleIndex = new List<int>(1);
                foreach (int surfaceIndex in indexes)
                {
                    int priority = perSurfacePriorities[surfaceIndex];
                    _chunk.MeshRenderer.SetSurfaceRenderPriority(surfaceIndex, priority);
                    var clonedMaterial = _chunk.GetRenderPriorityMaterial(request.Map.FilteredMaterial, priority);
                    var clonedMap = new MaterialMap(clonedMaterial, request.Map.FilteredPropertyBlock);
                    singleIndex.Clear();
                    singleIndex.Add(surfaceIndex);
                    UpdateMaterial(request.Key, singleIndex, in clonedMap);
                }
            }

            RemoveUnusedMaterials(activeMaterials);
        }

        // Godot caps per-surface render_priority at [-128..127] and sorts transparent surfaces by it
        // first, so coplanar UI quads need DISTINCT priorities or they fall back to distance sort (the
        // angle "wash"). We partition the range into bands so chunks layer deterministically even when
        // they overlap:
        //   root chunk        -> [-128 .. -1]   (below everything)
        //   normal nested     -> [   0 ..  63]   (above root; chunks don't overlap each other)
        //   overlay level 1   -> [  64 ..  71]   (e.g. a modal's dim/blur backdrop)
        //   overlay level 2   -> [  72 ..  87]   (e.g. the modal panel background)
        //   overlay level >=3 -> [  88 .. 127]   (modal content/rows, on top)
        // Within a band, surfaces pack high in submission order (later = drawn on top). - xlinka
        private int[] BuildPerSurfacePriorities(int materialCount, bool renderOnTop, int overlayLevel)
        {
            var result = new int[materialCount];
            if (materialCount == 0) return result;

            int lo, hi;
            if (!renderOnTop)
            {
                lo = GodotRenderPriorityMin; hi = -1;          // root chunk
            }
            else if (overlayLevel <= 0)
            {
                lo = 0; hi = 63;                                // normal nested chunk
            }
            else if (overlayLevel == 1)
            {
                lo = 64; hi = 71;                               // overlay backdrop
            }
            else if (overlayLevel == 2)
            {
                lo = 72; hi = 87;                               // overlay panel
            }
            else
            {
                lo = 88; hi = GodotRenderPriorityMax;           // overlay content
            }

            // When a chunk has more surfaces than its band is wide, the fill loop below saturates the
            // overflow at hi - those coplanar quads collapse onto one render_priority and Godot falls back to
            // distance sort (the angle "wash"). Surface that latent z-order bug in dev instead of shipping it
            // as a mystery visual glitch. Warn once per RenderData; near-exhaustion is debug-only. -xlinka
            int bandWidth = hi - lo + 1;
            if (materialCount > bandWidth)
            {
                if (!_loggedBandExhaustion)
                {
                    _loggedBandExhaustion = true;
                    Logger.Warn($"[Helio] Render-priority band exhausted on chunk #{_chunk.OrderIndex} " +
                        $"(renderOnTop={renderOnTop}, overlayLevel={overlayLevel}): {materialCount} surfaces > band width " +
                        $"{bandWidth} [{lo}..{hi}]. {materialCount - bandWidth} surface(s) saturate at {hi}, so coplanar " +
                        $"z-order goes ambiguous. Reduce overlay nesting / material count, or move this canvas to unbounded render order.");
                }
            }
            else if (materialCount > bandWidth - 2 && Logger.EnableDebug)
            {
                Logger.Debug($"[Helio] Render-priority band near exhaustion on chunk #{_chunk.OrderIndex}: " +
                    $"{materialCount}/{bandWidth} in [{lo}..{hi}].");
            }

            int start = hi - materialCount + 1;
            if (start < lo) start = lo;
            for (int i = 0; i < materialCount; i++)
            {
                result[i] = System.Math.Min(hi, start + i);
            }
            return result;
        }

        private List<MaterialRequest> BuildMaterialRequests(
            out Dictionary<PhosTriangleSubmesh, int> submeshQueues)
        {
            var requests = new List<MaterialRequest>(_requestedMaterials.Count);
            submeshQueues = new Dictionary<PhosTriangleSubmesh, int>();

            foreach (var pair in _requestedMaterials)
            {
                var map = GetMaterialMap(pair.Key);
                int logicalQueue = GetLogicalRenderQueue(map.FilteredMaterial);

                foreach (var submesh in pair.Value)
                {
                    if (submesh.IndexCount > 0)
                    {
                        submeshQueues[submesh] = logicalQueue;
                    }
                }

                requests.Add(new MaterialRequest(pair.Key, pair.Value, in map, logicalQueue));
            }

            return requests;
        }

        private void SortSubmeshesByRenderQueue(Dictionary<PhosTriangleSubmesh, int> submeshQueues)
        {
            // Stencil-WRITE submeshes must draw before any stencil-tested content (which compares against the
            // stencil they stamp), regardless of logical render queue. Without this, content with a custom low
            // RenderQueue could sort ahead of the mask writer and then be discarded by compare_equal. -xlinka
            HashSet<PhosTriangleSubmesh>? writeSubmeshes = null;
            foreach (var pair in _requestedMaterials)
            {
                if (pair.Key.Stencil != StencilRole.Write)
                {
                    continue;
                }

                writeSubmeshes ??= new HashSet<PhosTriangleSubmesh>();
                foreach (var submesh in pair.Value)
                {
                    writeSubmeshes.Add(submesh);
                }
            }

            if (Mesh.Submeshes.Count < 2 || (submeshQueues.Count < 2 && writeSubmeshes == null))
            {
                return;
            }

            var originalIndexes = new Dictionary<PhosSubmesh, int>(Mesh.Submeshes.Count);
            for (int i = 0; i < Mesh.Submeshes.Count; i++)
            {
                originalIndexes[Mesh.Submeshes[i]] = i;
            }

            Mesh.Submeshes.Sort((left, right) =>
            {
                // Writers first, always.
                bool leftWrite = writeSubmeshes != null && left is PhosTriangleSubmesh lw && writeSubmeshes.Contains(lw);
                bool rightWrite = writeSubmeshes != null && right is PhosTriangleSubmesh rw && writeSubmeshes.Contains(rw);
                if (leftWrite != rightWrite)
                {
                    return leftWrite ? -1 : 1;
                }

                int leftQueue = left is PhosTriangleSubmesh leftTriangle
                    && submeshQueues.TryGetValue(leftTriangle, out int lq)
                        ? lq
                        : DefaultLogicalRenderQueue;
                int rightQueue = right is PhosTriangleSubmesh rightTriangle
                    && submeshQueues.TryGetValue(rightTriangle, out int rq)
                        ? rq
                        : DefaultLogicalRenderQueue;

                int queueCompare = leftQueue.CompareTo(rightQueue);
                return queueCompare != 0
                    ? queueCompare
                    : originalIndexes[left].CompareTo(originalIndexes[right]);
            });
        }

        private static int GetLogicalRenderQueue(IAssetProvider<MaterialAsset>? material)
        {
            int queue = material switch
            {
                UIUnlitMaterial ui => ui.RenderQueue.Value,
                UITextMaterial text => text.RenderQueue.Value,
                { IsAssetAvailable: true } => material.Asset.RenderQueue,
                _ => DefaultLogicalRenderQueue,
            };

            return queue >= 0 ? queue : DefaultLogicalRenderQueue;
        }

        private Dictionary<PhosTriangleSubmesh, int> BuildSurfaceIndexes()
        {
            var indexes = new Dictionary<PhosTriangleSubmesh, int>();
            int surfaceIndex = 0;
            foreach (var submesh in Mesh.Submeshes)
            {
                if (submesh is not PhosTriangleSubmesh triangleSubmesh || triangleSubmesh.IndexCount <= 0)
                {
                    continue;
                }

                indexes[triangleSubmesh] = surfaceIndex++;
            }

            return indexes;
        }

        private void UpdateMaterial(in MaterialKey key, List<int> indexes, in MaterialMap map)
        {
            if (_assignedMaterials.TryGetValue(key, out var assigned))
            {
                bool indexChanged = !SameIndexes(assigned.Indexes, indexes);
                bool mapChanged = assigned.Map != map;
                if (!indexChanged && !mapChanged)
                {
                    SetMaterialTargets(indexes, in map);
                    return;
                }

                assigned.Indexes.Clear();
                assigned.Indexes.AddRange(indexes);
                SetMaterialTargets(assigned.Indexes, in map);
                _assignedMaterials[key] = new AssignedMaterial(assigned.Indexes, in map);
                return;
            }

            SetMaterialTargets(indexes, in map);
            _assignedMaterials.Add(key, new AssignedMaterial(indexes, in map));
        }

        private static bool SameIndexes(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void RemoveUnusedMaterials(HashSet<MaterialKey> activeMaterials)
        {
            if (_assignedMaterials.Count == activeMaterials.Count)
            {
                bool allFound = true;
                foreach (var key in _assignedMaterials.Keys)
                {
                    if (!activeMaterials.Contains(key))
                    {
                        allFound = false;
                        break;
                    }
                }
                if (allFound)
                {
                    return;
                }
            }

            List<MaterialKey>? remove = null;
            foreach (var pair in _assignedMaterials)
            {
                if (activeMaterials.Contains(pair.Key))
                {
                    continue;
                }

                remove ??= new List<MaterialKey>();
                remove.Add(pair.Key);
            }

            if (remove == null)
            {
                return;
            }

            foreach (var key in remove)
            {
                _assignedMaterials.Remove(key);
            }
        }

        private void SetMaterialTargets(IReadOnlyList<int> indexes, in MaterialMap map)
        {
            for (int i = 0; i < indexes.Count; i++)
            {
                SetMaterialTargets(indexes[i], in map);
            }
        }

        private void SetMaterialTargets(int index, in MaterialMap map)
        {
            var materialRef = _chunk.MeshRenderer!.Materials.GetElement(index);
            var material = map.FilteredMaterial;
            if (!ReferenceEquals(materialRef.Target, material))
            {
                materialRef.Target = material!;
            }

            var propertyBlockRef = _chunk.MeshRenderer.MaterialPropertyBlocks.GetElement(index);
            var propertyBlock = map.FilteredPropertyBlock;
            if (!ReferenceEquals(propertyBlockRef.Target, propertyBlock))
            {
                propertyBlockRef.Target = propertyBlock!;
            }
        }

        private void EnsureComponents()
        {
            _chunk.MeshProvider ??= _chunk.ChunkSlot.GetComponent<LocalMeshSource>() ?? _chunk.ChunkSlot.AttachComponent<LocalMeshSource>();
            _chunk.MeshRenderer ??= _chunk.ChunkSlot.GetComponent<MeshRenderer>() ?? _chunk.ChunkSlot.AttachComponent<MeshRenderer>();
            _chunk.MeshRenderer.Mesh.Target = _chunk.MeshProvider;
        }

        private MaterialMap GetMaterialMap(in MaterialKey key)
        {
            var baseMaterial = key.BaseMaterial ?? _chunk.GetDefaultUIMaterial();
            bool usingDefault = key.BaseMaterial == null;

            MaterialMap map;
            if (key.Mapper != null)
            {
                map = key.Mapper(this, baseMaterial, key.Key, usingDefault);
            }
            else
            {
                map = new MaterialMap(baseMaterial);
            }

            if (key.Stencil != StencilRole.None)
            {
                // Mask shape (Write) or stencil-tested content (Test): a shader-variant clone, with the
                // inherited rect clip folded in as an orthogonal AABB bound alongside the stencil shape.
                return new MaterialMap(
                    _chunk.GetStencilMaterial(map.FilteredMaterial, key.Stencil, key.ClipRect),
                    map.FilteredPropertyBlock);
            }

            if (!key.ClipRect.HasValue)
            {
                return map;
            }

            return new MaterialMap(_chunk.GetClippedMaterial(map.FilteredMaterial, key.ClipRect.Value), map.FilteredPropertyBlock);
        }

        private void PrepareMesh()
        {
            Mesh.HasColors = true;
            Mesh.SetHasUV(0, true);
        }

        private readonly struct MaterialRequest
        {
            public readonly MaterialKey Key;
            public readonly List<PhosTriangleSubmesh> Submeshes;
            public readonly MaterialMap Map;
            public readonly int LogicalRenderQueue;

            public MaterialRequest(
                in MaterialKey key,
                List<PhosTriangleSubmesh> submeshes,
                in MaterialMap map,
                int logicalRenderQueue)
            {
                Key = key;
                Submeshes = submeshes;
                Map = map;
                LogicalRenderQueue = logicalRenderQueue;
            }
        }
    }

    public Canvas Canvas { get; }
    public RectTransform Root { get; }
    public Slot ChunkSlot { get; private set; } = null!;
    public LocalMeshSource? MeshProvider { get; private set; }
    public MeshRenderer? MeshRenderer { get; private set; }
    public RenderData ContentRenderData { get; }

    // Nested chunks render above the root chunk: their surfaces pack into the top render_priority band. - xlinka
    public bool RenderOnTop { get; set; }

    // Render-space translation of this chunk's whole mesh, applied on the chunk slot. ScrollRect uses it to
    // slide scrolled content without touching its rect; the canvas counter-translates the clip to keep the
    // viewport window fixed. Canvas pixels, in the chunk's (canvas-local) frame. -xlinka
    public float2 ComputeOffset { get; private set; }

    public void SetComputeOffset(float2 offset)
    {
        if (ComputeOffset == offset && ChunkSlot != null && !ChunkSlot.IsDestroyed)
            return;
        ComputeOffset = offset;
        SetupComponents();
        if (ChunkSlot != null && !ChunkSlot.IsDestroyed)
            ChunkSlot.LocalPosition.Value = new float3(offset.x, offset.y, 0f);
    }

    // 0 = normal nested chunk. > 0 = overlay layer: reserves a render-priority band above all
    // normal chunks so a modal draws on top of everything (see BuildPerSurfacePriorities). - xlinka
    public int OverlayLevel { get; set; }

    // Tree-order sort index, assigned by the Canvas in hierarchy order on each render-root cycle. Drives the
    // mesh renderer's SortingOrder (Godot sorting_offset), which is the INNER transparent-sort key under
    // render_priority. So this gives later-in-tree chunks a higher offset -> they tie-break ON TOP of earlier
    // ones that share a render_priority value (previously all nested chunks shared one offset, leaving
    // same-band overlap undefined). Stored on the chunk so a scoped re-mesh keeps its place. -xlinka
    public int OrderIndex { get; set; }

    private UIUnlitMaterial? _defaultMaterial;
    private MaterialCloneCache? _materialCloneCache;
    private float2 _lastClipOffset;
    private bool _hasClipOffset;

    public GraphicsChunk(Canvas canvas, RectTransform root)
    {
        Canvas = canvas;
        Root = root;
        ContentRenderData = new RenderData(this);
    }

    public void SetupComponents()
    {
        ChunkSlot ??= Canvas.Slot.AddLocalSlot("GraphicsChunk");
    }

    // Pin this chunk's clip window as it slides (render-offset scrolling): pushes the scroll offset into the
    // clip_offset uniform on every cloned material, so the fixed canvas-space clip rect clips the moved
    // content correctly. No mesh touched. -xlinka
    // persist=false: live scroll (cheap direct shader-param write). persist=true: after a structural rebuild,
    // also write the material Sync so its own update doesn't reset the offset. -xlinka
    public void SetClipOffset(float2 offset, bool persist)
    {
        // Skip the per-clone write when the offset is unchanged: a live scroll re-pushes to every material in
        // the chunk, so a momentum-settle frame or an idle repaint at the same position would churn one
        // SetShaderParameter per card for nothing. A rebuild (persist) always applies - the clones were just
        // re-created and need re-pinning to the current position. -xlinka
        if (!persist && _hasClipOffset && _lastClipOffset == offset)
            return;
        _lastClipOffset = offset;
        _hasClipOffset = true;
        _materialCloneCache?.SetClipOffset(offset, persist);
    }

    // Shared per-atlas/per-texture materials live on the CANVAS now: with per-row chunk roots (an
    // inspector panel has 70+), per-chunk caches duplicated every atlas material and its
    // render-priority clones once per row. One canvas-wide material per atlas/texture. - xlinka
    public UITextMaterial GetSharedTextMaterial(TextureAsset atlas) => Canvas.GetSharedTextMaterial(atlas);

    public MainTexturePropertyBlock GetSharedImageBlock(IAssetProvider<TextureAsset> texture) => Canvas.GetSharedImageBlock(texture);

    public UIUnlitMaterial GetSharedImageMaterial(IAssetProvider<TextureAsset> texture) => Canvas.GetSharedImageMaterial(texture);

    public IAssetProvider<MaterialAsset> GetDefaultUIMaterial()
    {
        SetupComponents();
        if (_defaultMaterial == null)
        {
            _defaultMaterial = ChunkSlot.GetComponent<UIUnlitMaterial>() ?? ChunkSlot.AttachComponent<UIUnlitMaterial>();
            _defaultMaterial.Culling.Value = Culling.None;
            _defaultMaterial.ZWrite.Value = ZWrite.Off;
            _defaultMaterial.RenderQueue.Value = 3000;
            if (Canvas.Overlay.Value)
                _defaultMaterial.ZTest.Value = ZTest.Always;
        }
        return _defaultMaterial;
    }

    public void PrepareCompute()
    {
        _materialCloneCache?.BeginFrame();
        ContentRenderData.PrepareCompute();
    }

    public void SubmitChanges(int sortingOrder = 0)
    {
        OrderIndex = sortingOrder;
        ContentRenderData.SubmitChanges(sortingOrder);
        _materialCloneCache?.EndFrame();
    }

    // Push this chunk's tree-order index to its renderer WITHOUT re-meshing. Used on a render-root cycle for
    // chunks that were seen but not recomputed (their mesh is unchanged, but their place in tree order may have
    // shifted because a sibling chunk was added/removed). Cheap - just sets the SortingOrder sync. -xlinka
    public void ApplyOrderToRenderer()
    {
        if (MeshRenderer != null && !MeshRenderer.IsDestroyed)
            MeshRenderer.SortingOrder.Value = OrderIndex;
    }

    // Show/hide a built chunk without rebuilding it. Hiding content disables the chunk slot
    // (its mesh persists); re-showing re-enables it instantly, vs disposing + re-tessellating
    // which is what made re-shown content pop in a frame late.
    public void SetActive(bool active)
    {
        if (ChunkSlot != null && !ChunkSlot.IsDestroyed)
            ChunkSlot.ActiveSelf.Value = active;
    }

    // Tear down a nested chunk whose GraphicChunkRoot was removed from the tree. - xlinka
    public void Dispose()
    {
        _materialCloneCache?.Destroy();
        _materialCloneCache = null;
        ChunkSlot?.Destroy();
    }

    private IAssetProvider<MaterialAsset>? GetClippedMaterial(IAssetProvider<MaterialAsset>? source, in Rect clipRect)
    {
        if (source == null)
        {
            return null;
        }

        SetupComponents();
        _materialCloneCache ??= new MaterialCloneCache(ChunkSlot);
        return _materialCloneCache.GetClippedMaterial(source, clipRect);
    }

    // Clone of a UI material that swaps to the stencil-write or stencil-test shader variant (and folds in the
    // inherited rect clip). Used for shaped GPU masking; the rect-clip path above is untouched. -xlinka
    private IAssetProvider<MaterialAsset>? GetStencilMaterial(IAssetProvider<MaterialAsset>? source, StencilRole role, Rect? clipRect)
    {
        if (source == null)
        {
            return null;
        }

        SetupComponents();
        _materialCloneCache ??= new MaterialCloneCache(ChunkSlot);
        return _materialCloneCache.GetStencilMaterial(source, role, clipRect);
    }

    // Per-surface render priority requires its own cloned material because the
    // MeshRendererHook mutates Material.RenderPriority in place. Without cloning,
    // all surfaces sharing the source material collapse to whichever priority
    // was written last -> coplanar UI quads fall back to Godot's distance sort
    // and flip order as the camera rotates ("colors lost at angle"). - xlinka
    internal IAssetProvider<MaterialAsset>? GetRenderPriorityMaterial(IAssetProvider<MaterialAsset>? source, int renderPriority)
    {
        if (source == null) return null;
        SetupComponents();
        _materialCloneCache ??= new MaterialCloneCache(ChunkSlot);
        return _materialCloneCache.GetRenderPriorityMaterial(source, renderPriority);
    }
}
