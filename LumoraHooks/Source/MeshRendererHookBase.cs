// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using LumoraMeshes = Lumora.Core.Components.Meshes;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

/// <summary>
/// Abstract base class for MeshRenderer hooks.
/// </summary>
public abstract class MeshRendererHookBase<T, U> : ComponentHook<T>
    where T : MeshRenderer
    where U : Node3D
{
    protected struct RendererData
    {
        public Node3D gameObject;
        public MeshInstance3D meshInstance;
        public U meshRenderer;
    }

    private MeshInstance3D meshInstance = null!;
    private int _lastSurfaceOverrideCount;

    protected abstract bool UseMeshInstance { get; }

    protected bool meshWasChanged { get; private set; }

    public U MeshRenderer { get; protected set; } = null!;

    protected abstract void AssignMesh(U renderer, Mesh mesh);

    protected virtual void OnAttachRenderer()
    {
        // Hide the source mesh's MeshInstance3D since we're taking over rendering
        HideSourceMeshInstance();
    }

    /// <summary>
    /// Hide the MeshInstance3D from the source mesh (ProceduralMesh) since MeshRenderer handles rendering.
    /// </summary>
    private void HideSourceMeshInstance()
    {
        var meshValue = Owner.Mesh.Target;
        if (meshValue is LumoraMeshes.ProceduralMesh proceduralMesh)
        {
            if (proceduralMesh.Hook is MeshHook meshHook)
            {
                var sourceMeshInstance = meshHook.GetMeshInstance();
                if (sourceMeshInstance != null)
                {
                    sourceMeshInstance.Visible = false;
                    LumoraLogger.Debug($"MeshRendererHookBase: Hid source MeshHook's MeshInstance3D");
                }
            }
        }
    }

    protected virtual void OnCleanupRenderer()
    {
    }

    // RenderLayerOverrideHook only walks nodes that exist when it applies, so mesh
    // instances created later (canvas chunks built after a parked dash reactivates)
    // would stay on the default layer - invisible to layer-masked capture cameras.
    // Inherit the override at creation instead.
    private void ApplyInheritedRenderLayer(VisualInstance3D visual)
    {
        for (var slot = Owner.Slot; slot != null; slot = slot.Parent)
        {
            var layerOverride = slot.GetComponent<RenderLayerOverride>();
            if (layerOverride != null && layerOverride.Enabled && layerOverride.Layer.Value != 0)
            {
                visual.Layers = (uint)layerOverride.Layer.Value;
                return;
            }
        }
    }

    public override void ApplyChanges()
    {
        // Not initialized yet (or already torn down) - nothing to attach under.
        if (attachedNode == null)
        {
            return;
        }

        if (Owner.Mesh.Target != null && IsMeshAssetAvailable())
        {
            if (MeshRenderer == null)
            {
                var gameObject = new Node3D();
                gameObject.Name = "MeshRenderer";
                attachedNode.AddChild(gameObject);

                if (UseMeshInstance)
                {
                    meshInstance = new MeshInstance3D();
                    ApplyInheritedRenderLayer(meshInstance);
                    gameObject.AddChild(meshInstance);
                }

                MeshRenderer = (gameObject as U)!;
                OnAttachRenderer();
            }

            meshWasChanged = Owner.Mesh.GetWasChangedAndClear();
            if (meshWasChanged)
            {
                // Hide the source mesh's MeshInstance3D when mesh changes
                HideSourceMeshInstance();

                var godotMesh = GetGodotMeshFromAsset();
                if (UseMeshInstance && meshInstance != null)
                {
                    meshInstance.Mesh = godotMesh;

                    // Debug: verify mesh has UV data
                    if (godotMesh is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0)
                    {
                        var arrays = arrayMesh.SurfaceGetArrays(0);
                        var uvArray = arrays[(int)Mesh.ArrayType.TexUV];
                        bool hasUvs = uvArray.VariantType != Variant.Type.Nil;
                        LumoraLogger.Log($"MeshRendererHookBase: Mesh assigned, surface 0 has UV data: {hasUvs}");
                        if (hasUvs)
                        {
                            var uvs = uvArray.AsVector2Array();
                            if (uvs.Length > 0)
                            {
                                LumoraLogger.Log($"MeshRendererHookBase: UV sample[0] = {uvs[0]}");
                            }
                        }
                    }
                }
                else
                {
                    AssignMesh(MeshRenderer, godotMesh);
                }
            }

            bool enabled = Owner.Enabled;
            if (meshInstance != null && meshInstance.Visible != enabled)
            {
                meshInstance.Visible = enabled;
            }

            if (Owner.SortingOrder.GetWasChangedAndClear())
            {
                ApplyRenderQueue();
            }

            if (Owner.ShadowCastMode.GetWasChangedAndClear())
            {
                ApplyShadowCastMode(Owner.ShadowCastMode.Value);
            }

            bool materialRefsChanged = Owner.MaterialsChanged
                || Owner.MaterialPropertyBlocksChanged
                || AnyChanged(Owner.Materials)
                || AnyChanged(Owner.MaterialPropertyBlocks);
            bool surfacePrioritiesChanged = Owner.SurfaceRenderPrioritiesChanged
                || AnyChanged(Owner.SurfaceRenderPriorities);
            bool surfaceAssignmentsChanged = Owner.SurfaceAssignmentsChanged || meshWasChanged;
            bool materialsChanged = materialRefsChanged || surfacePrioritiesChanged || surfaceAssignmentsChanged;

            if (materialsChanged)
            {
                Owner.MaterialsChanged = false;
                Owner.MaterialPropertyBlocksChanged = false;
                Owner.SurfaceRenderPrioritiesChanged = false;
                Owner.SurfaceAssignmentsChanged = false;
                ApplyMaterials();
                ApplyRenderQueue();
            }
        }
        else
        {
            CleanupRenderer(destroyingWorld: false);
        }
    }

    protected virtual void ApplyMaterials()
    {
        if (meshInstance == null) return;

        int count = GetSurfaceCount();

        for (int i = 0; i < count; i++)
        {
            meshInstance.SetSurfaceOverrideMaterial(i, GetSurfaceMaterial(i));
        }

        meshInstance.MaterialOverride = null;
        _lastSurfaceOverrideCount = count;

        if (_uiDiagLogged < 3 && Owner.Slot?.Parent?.SlotName?.Value == "HelioTestPanel")
        {
            _uiDiagLogged++;
            int assigned = 0;
            for (int i = 0; i < count; i++)
            {
                if (meshInstance.GetSurfaceOverrideMaterial(i) != null) assigned++;
            }
            LumoraLogger.Log(
                $"MeshRendererHook.ApplyMaterials[diag]: slot={Owner.Slot?.SlotName.Value} " +
                $"surfaces={count} materials(list)={Owner.Materials.Count} " +
                $"overrides(non-null)={assigned}");
        }
    }

    private int _uiDiagLogged;

    private Material GetSurfaceMaterial(int index)
    {
        var materialAsset = GetMaterialAsset(index);
        if (materialAsset == null)
        {
            return null!;
        }

        var propertyBlock = GetPropertyBlockAsset(index);
        if (propertyBlock != null && propertyBlock.IsValid)
        {
            return ApplySurfaceRenderPriority(index, (propertyBlock.ApplyToMaterial(materialAsset) as Material)!);
        }

        return ApplySurfaceRenderPriority(index, (materialAsset.GodotMaterial as Material)!);
    }

    private Material ApplySurfaceRenderPriority(int index, Material material)
    {
        if (material == null)
        {
            return null!;
        }

        int priority = Owner.GetSurfaceRenderPriority(index);
        if (priority == Lumora.Core.Components.MeshRenderer.NoSurfaceRenderPriority)
        {
            return material;
        }

        if (material.RenderPriority == priority)
        {
            return material;
        }

        MaterialPropertyApplicator.ApplyGodotRenderPriority(material, priority);
        return material;
    }

    private MaterialAsset GetMaterialAsset(int index)
    {
        if (Owner.Materials.Count == 0)
        {
            return null!;
        }

        int materialIndex = System.Math.Min(index, Owner.Materials.Count - 1);
        return Owner.Materials.GetElement(materialIndex).Asset;
    }

    private MaterialPropertyBlockAsset GetPropertyBlockAsset(int index)
    {
        if (Owner.MaterialPropertyBlocks.Count == 0 || index >= Owner.MaterialPropertyBlocks.Count)
        {
            return null!;
        }

        return Owner.MaterialPropertyBlocks.GetElement(index).Asset;
    }

    private int GetSurfaceCount()
    {
        return meshInstance?.Mesh?.GetSurfaceCount() ?? 0;
    }

    private static bool AnyChanged<A>(SyncAssetList<A> list) where A : Asset
    {
        bool changed = false;
        foreach (var assetRef in list)
        {
            changed |= assetRef.GetWasChangedAndClear();
        }

        return changed;
    }

    private static bool AnyChanged(SyncIntList list)
    {
        bool changed = false;
        foreach (var field in list)
        {
            changed |= field.GetWasChangedAndClear();
        }

        return changed;
    }

    private void CleanupRenderer(bool destroyingWorld)
    {
        if (!destroyingWorld && MeshRenderer != null && GodotObject.IsInstanceValid(MeshRenderer))
        {
            ((Node)MeshRenderer).QueueFree();
        }
        OnCleanupRenderer();
    }

    public override void Destroy(bool destroyingWorld)
    {
        CleanupRenderer(destroyingWorld);
        meshInstance = null!;
        MeshRenderer = default!;
        base.Destroy(destroyingWorld);
    }

    protected virtual bool IsMeshAssetAvailable()
    {
        return Owner.Mesh.Target != null;
    }

    protected virtual Mesh GetGodotMeshFromAsset()
    {
        var meshComponent = Owner.Mesh.Target;
        if (meshComponent == null) return null!;

        // Handle ProceduralMesh components - get mesh from their hook
        if (meshComponent is LumoraMeshes.ProceduralMesh proceduralMesh)
        {
            if (proceduralMesh.Hook is MeshHook meshHook)
            {
                var meshInstance = meshHook.GetMeshInstance();
                if (meshInstance?.Mesh != null)
                {
                    return meshInstance.Mesh;
                }
            }
            LumoraLogger.Debug("MeshRendererHookBase: ProceduralMesh hook not ready");
            return null!;
        }

        // TODO: Handle MeshDataAssetProvider components when needed
        LumoraLogger.Warn($"MeshRendererHookBase: Unsupported mesh component type {meshComponent.GetType().Name}");
        return null!;
    }

    protected virtual void ApplySortingOrder(int sortingOrder)
    {
        ApplyRenderQueue();
    }

    protected virtual void ApplyRenderQueue()
    {
        if (meshInstance == null)
        {
            return;
        }

        var materialQueue = Owner.Material.Asset?.RenderQueue ?? -1;
        var effectiveQueue = (materialQueue >= 0 ? materialQueue : 0) + Owner.SortingOrder.Value;

        // Godot uses SortingOffset for transparent object ordering, while Material.RenderPriority
        // applies per-material. Keep them in sync so Lumora's material RenderQueue affects the
        // renderer as well as the material resource.
        meshInstance.SortingOffset = effectiveQueue;
    }

    protected virtual void ApplyShadowCastMode(ShadowCastMode shadowCastMode)
    {
        if (meshInstance != null)
        {
            switch (shadowCastMode)
            {
                case ShadowCastMode.Off:
                    meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    break;
                case ShadowCastMode.On:
                    meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                    break;
                case ShadowCastMode.ShadowOnly:
                    meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
                    break;
                case ShadowCastMode.DoubleSided:
                    meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
                    break;
            }
        }
    }

}

