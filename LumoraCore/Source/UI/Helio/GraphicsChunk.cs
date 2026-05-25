// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
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
        private const int GodotRenderPriorityCount = GodotRenderPriorityMax - GodotRenderPriorityMin + 1;

        private readonly Dictionary<MaterialKey, List<PhosTriangleSubmesh>> _requestedMaterials = new();
        private readonly Dictionary<MaterialKey, AssignedMaterial> _assignedMaterials = new();
        private readonly GraphicsChunk _chunk;
        private int _minimumSubmeshIndex;
        private Rect? _clipRect;

        public PhosMesh Mesh { get; } = new();
        public Rect? ClipRect => _clipRect;

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
            _minimumSubmeshIndex = 0;
            PrepareMesh();
        }

        public void BeginGraphic()
        {
            _minimumSubmeshIndex = Mesh.Submeshes.Count;
        }

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material)
            => GetSubmesh(new MaterialKey(material, null, null, _clipRect));

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material, MaterialMapper mapper)
            => GetSubmesh(new MaterialKey(material, null, mapper, _clipRect));

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material, object? key, MaterialMapper? mapper)
            => GetSubmesh(new MaterialKey(material, key, mapper, _clipRect));

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

        internal void SubmitChanges(int sortingOrder)
        {
            _chunk.SetupComponents();
            EnsureComponents();
            AssignMaterials();

            _chunk.MeshRenderer!.SortingOrder.Value = sortingOrder;
            _chunk.MeshRenderer.Enabled.Value = Mesh.VertexCount > 0;
            _chunk.MeshProvider!.SetMesh(Mesh);

            // MeshHook will ClearSurfaces + AddSurfaceFromArrays the underlying ArrayMesh, which
            // wipes Godot's surface override materials. The MeshRenderer's own SyncRef sees no
            // change, so we have to flag it ourselves or every surface past frame 0 renders bare. - xlinka
            _chunk.MeshRenderer.FlagSurfacesDirty();
        }

        private void AssignMaterials()
        {
            var requests = BuildMaterialRequests(out var submeshQueues, out var logicalQueues);
            SortSubmeshesByRenderQueue(submeshQueues);

            var surfaceIndexes = BuildSurfaceIndexes();
            int materialCount = surfaceIndexes.Count;
            _chunk.MeshRenderer!.Materials.EnsureExactCount(materialCount);
            _chunk.MeshRenderer.MaterialPropertyBlocks.EnsureExactCount(materialCount);
            _chunk.MeshRenderer.EnsureExactSurfaceRenderPriorityCount(materialCount);

            var renderPriorityMap = BuildRenderPriorityMap(logicalQueues);
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

                int renderPriority = renderPriorityMap.TryGetValue(request.LogicalRenderQueue, out var priority)
                    ? priority
                    : 0;
                foreach (int index in indexes)
                {
                    _chunk.MeshRenderer.SetSurfaceRenderPriority(index, MeshRenderer.NoSurfaceRenderPriority);
                }

                var map = ApplyRenderPriority(request.Map, renderPriority);
                activeMaterials.Add(request.Key);
                UpdateMaterial(request.Key, indexes, in map);
            }

            RemoveUnusedMaterials(activeMaterials);
        }

        private List<MaterialRequest> BuildMaterialRequests(
            out Dictionary<PhosTriangleSubmesh, int> submeshQueues,
            out SortedSet<int> logicalQueues)
        {
            var requests = new List<MaterialRequest>(_requestedMaterials.Count);
            submeshQueues = new Dictionary<PhosTriangleSubmesh, int>();
            logicalQueues = new SortedSet<int>();

            foreach (var pair in _requestedMaterials)
            {
                var map = GetMaterialMap(pair.Key);
                int logicalQueue = GetLogicalRenderQueue(map.FilteredMaterial);
                logicalQueues.Add(logicalQueue);

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
            if (submeshQueues.Count < 2)
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

        private static Dictionary<int, int> BuildRenderPriorityMap(SortedSet<int> logicalQueues)
        {
            var map = new Dictionary<int, int>(logicalQueues.Count);
            if (logicalQueues.Count == 0)
            {
                return map;
            }

            int index = 0;
            if (logicalQueues.Count <= GodotRenderPriorityCount)
            {
                int start = -logicalQueues.Count / 2;
                foreach (int queue in logicalQueues)
                {
                    map[queue] = System.Math.Clamp(start + index, GodotRenderPriorityMin, GodotRenderPriorityMax);
                    index++;
                }
                return map;
            }

            foreach (int queue in logicalQueues)
            {
                double t = logicalQueues.Count == 1 ? 0d : index / (double)(logicalQueues.Count - 1);
                map[queue] = GodotRenderPriorityMin + (int)System.Math.Round(t * (GodotRenderPriorityCount - 1));
                index++;
            }

            return map;
        }

        private MaterialMap ApplyRenderPriority(in MaterialMap map, int renderPriority)
        {
            var material = _chunk.GetRenderPriorityMaterial(map.FilteredMaterial, renderPriority);
            return new MaterialMap(material, map.FilteredPropertyBlock);
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
                materialRef.Target = material;
            }

            var propertyBlockRef = _chunk.MeshRenderer.MaterialPropertyBlocks.GetElement(index);
            var propertyBlock = map.FilteredPropertyBlock;
            if (!ReferenceEquals(propertyBlockRef.Target, propertyBlock))
            {
                propertyBlockRef.Target = propertyBlock;
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

    private UIUnlitMaterial? _defaultMaterial;
    private MaterialCloneCache? _materialCloneCache;

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

    public IAssetProvider<MaterialAsset> GetDefaultUIMaterial()
    {
        SetupComponents();
        if (_defaultMaterial == null)
        {
            _defaultMaterial = ChunkSlot.GetComponent<UIUnlitMaterial>() ?? ChunkSlot.AttachComponent<UIUnlitMaterial>();
            _defaultMaterial.Culling.Value = Culling.None;
            _defaultMaterial.ZWrite.Value = ZWrite.Off;
            _defaultMaterial.RenderQueue.Value = 3000;
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
        ContentRenderData.SubmitChanges(sortingOrder);
        _materialCloneCache?.EndFrame();
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

    private IAssetProvider<MaterialAsset>? GetRenderPriorityMaterial(IAssetProvider<MaterialAsset>? source, int renderPriority)
    {
        if (source == null)
        {
            return null;
        }

        SetupComponents();
        _materialCloneCache ??= new MaterialCloneCache(ChunkSlot);
        return _materialCloneCache.GetRenderPriorityMaterial(source, renderPriority);
    }
}
