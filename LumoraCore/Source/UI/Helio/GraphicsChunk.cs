// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Phos;

namespace Helio.UI;

public sealed class GraphicsChunk
{
    public sealed class RenderData
    {
        private readonly Dictionary<MaterialKey, List<PhosTriangleSubmesh>> _requestedMaterials = new();
        private readonly Dictionary<MaterialKey, AssignedMaterial> _assignedMaterials = new();
        private readonly GraphicsChunk _chunk;
        private int _minimumSubmeshIndex;

        public PhosMesh Mesh { get; } = new();

        public RenderData(GraphicsChunk chunk)
        {
            _chunk = chunk;
            PrepareMesh();
        }

        public IReadOnlyDictionary<MaterialKey, AssignedMaterial> AssignedMaterials => _assignedMaterials;

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
            => GetSubmesh(new MaterialKey(material, null, null));

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material, MaterialMapper mapper)
            => GetSubmesh(new MaterialKey(material, null, mapper));

        public PhosTriangleSubmesh GetSubmesh(IAssetProvider<MaterialAsset>? material, object? key, MaterialMapper? mapper)
            => GetSubmesh(new MaterialKey(material, key, mapper));

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
            int materialCount = Mesh.Submeshes.Count;
            _chunk.MeshRenderer!.Materials.EnsureExactCount(materialCount);
            _chunk.MeshRenderer.MaterialPropertyBlocks.EnsureExactCount(materialCount);

            foreach (var pair in _requestedMaterials)
            {
                var map = GetMaterialMap(pair.Key);
                var indexes = new List<int>(pair.Value.Count);
                foreach (var submesh in pair.Value)
                {
                    indexes.Add(submesh.Index);
                }

                UpdateMaterial(pair.Key, indexes, in map);
            }

            RemoveUnusedMaterials();
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

        private void RemoveUnusedMaterials()
        {
            if (_assignedMaterials.Count == _requestedMaterials.Count)
            {
                bool allFound = true;
                foreach (var key in _assignedMaterials.Keys)
                {
                    if (!_requestedMaterials.ContainsKey(key))
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
                if (_requestedMaterials.ContainsKey(pair.Key))
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

            if (key.Mapper != null)
            {
                return key.Mapper(this, baseMaterial, key.Key, usingDefault);
            }

            return new MaterialMap(baseMaterial);
        }

        private void PrepareMesh()
        {
            Mesh.HasColors = true;
            Mesh.SetHasUV(0, true);
        }
    }

    public Canvas Canvas { get; }
    public RectTransform Root { get; }
    public Slot ChunkSlot { get; private set; } = null!;
    public LocalMeshSource? MeshProvider { get; private set; }
    public MeshRenderer? MeshRenderer { get; private set; }
    public RenderData ContentRenderData { get; }

    private UIUnlitMaterial? _defaultMaterial;

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
        ContentRenderData.PrepareCompute();
    }

    public void SubmitChanges(int sortingOrder = 0)
    {
        ContentRenderData.SubmitChanges(sortingOrder);
    }
}
