// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Networking.Sync;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

public sealed class SyncIntList : SyncList<Sync<int>>
{
    public event Action<SyncIntList>? OnChanged;

    public SyncIntList(Component owner)
    {
        Parent = owner;
        ElementsAdded += OnElementsAdded;
        ElementsRemoving += OnElementsRemoving;
    }

    public int GetValue(int index, int fallback)
    {
        return index >= 0 && index < Count ? GetElement(index).Value : fallback;
    }

    public void SetValue(int index, int value)
    {
        EnsureMinimumCount(index + 1);
        GetElement(index).Value = value;
    }

    public bool EnsureExactCount(int count, int defaultValue)
    {
        int oldCount = Count;
        base.EnsureExactCount(count);
        for (int i = oldCount; i < Count; i++)
        {
            GetElement(i).Value = defaultValue;
        }

        return oldCount != Count;
    }

    private void OnElementsAdded(SyncElementList<Sync<int>> list, int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list[index + i].OnChanged += OnElementChanged;
        }
        OnChanged?.Invoke(this);
    }

    private void OnElementsRemoving(SyncElementList<Sync<int>> list, int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list[index + i].OnChanged -= OnElementChanged;
        }
        OnChanged?.Invoke(this);
    }

    private void OnElementChanged(int value)
    {
        OnChanged?.Invoke(this);
    }
}

/// <summary>
/// Renders a 3D mesh.
/// </summary>
[ComponentCategory("Rendering")]
public class MeshRenderer : ImplementableComponent
{
    public const int NoSurfaceRenderPriority = int.MinValue;

    /// <summary>
    /// The mesh to render (ProceduralMesh or MeshDataAsset component).
    /// Uses SyncRef to properly sync component references over network.
    /// </summary>
    public readonly SyncRef<Component> Mesh;

    public readonly SyncAssetList<MaterialAsset> Materials;
    public readonly SyncAssetList<MaterialPropertyBlockAsset> MaterialPropertyBlocks;
    public readonly SyncIntList SurfaceRenderPriorities;

    /// <summary>
    /// Shadow casting mode (Off, On, ShadowOnly, DoubleSided).
    /// </summary>
    public readonly Sync<ShadowCastMode> ShadowCastMode;

    /// <summary>
    /// Sorting order for transparent rendering (lower values render first).
    /// </summary>
    public readonly Sync<int> SortingOrder;

    public bool MaterialsChanged { get; set; }
    public bool MaterialPropertyBlocksChanged { get; set; }
    public bool SurfaceRenderPrioritiesChanged { get; set; }
    public bool SurfaceAssignmentsChanged { get; set; }

    // Mesh DATA changing (ClearSurfaces + re-add) is invisible to the SyncRef WasChanged path,
    // so callers that rebuild the underlying ArrayMesh contents in place must flag us dirty
    // explicitly or surface override materials end up stale. - xlinka
    public void FlagSurfacesDirty()
    {
        SurfaceAssignmentsChanged = true;
        RunApplyChanges();
    }

    public void EnsureExactSurfaceRenderPriorityCount(int count)
    {
        if (SurfaceRenderPriorities.EnsureExactCount(count, NoSurfaceRenderPriority))
        {
            SurfaceRenderPrioritiesChanged = true;
        }
    }

    public void SetSurfaceRenderPriority(int index, int priority)
    {
        if (SurfaceRenderPriorities.GetValue(index, NoSurfaceRenderPriority) == priority)
        {
            return;
        }

        SurfaceRenderPriorities.SetValue(index, priority);
        SurfaceRenderPrioritiesChanged = true;
    }

    public int GetSurfaceRenderPriority(int index)
    {
        return SurfaceRenderPriorities.GetValue(index, NoSurfaceRenderPriority);
    }

    public AssetRef<MaterialAsset> Material
    {
        get
        {
            if (Materials.Count == 0)
            {
                return Materials.Add();
            }

            return Materials.GetElement(0);
        }
    }

    public MeshRenderer()
    {
        Mesh = new SyncRef<Component>(this);
        Materials = new SyncAssetList<MaterialAsset>(this);
        MaterialPropertyBlocks = new SyncAssetList<MaterialPropertyBlockAsset>(this);
        SurfaceRenderPriorities = new SyncIntList(this);
        ShadowCastMode = new Sync<ShadowCastMode>(this, Components.ShadowCastMode.On);
        SortingOrder = new Sync<int>(this, 0);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Materials.OnChanged += OnMaterialsChanged;
        MaterialPropertyBlocks.OnChanged += OnMaterialPropertyBlocksChanged;
        SurfaceRenderPriorities.OnChanged += OnSurfaceRenderPrioritiesChanged;
        LumoraLogger.Log($"MeshRenderer: Awake on slot '{Slot.SlotName.Value}'");
    }

    private void OnMaterialsChanged(SyncAssetList<MaterialAsset> list)
    {
        MaterialsChanged = true;
    }

    private void OnMaterialPropertyBlocksChanged(SyncAssetList<MaterialPropertyBlockAsset> list)
    {
        MaterialPropertyBlocksChanged = true;
    }

    private void OnSurfaceRenderPrioritiesChanged(SyncIntList list)
    {
        SurfaceRenderPrioritiesChanged = true;
    }

    public override void OnDestroy()
    {
        Materials.OnChanged -= OnMaterialsChanged;
        MaterialPropertyBlocks.OnChanged -= OnMaterialPropertyBlocksChanged;
        SurfaceRenderPriorities.OnChanged -= OnSurfaceRenderPrioritiesChanged;
        base.OnDestroy();
        LumoraLogger.Log($"MeshRenderer: Destroyed on slot '{Slot?.SlotName.Value}'");
    }
}

/// <summary>
/// Shadow casting modes for MeshRenderer.
/// </summary>
public enum ShadowCastMode
{
    Off = 0,
    On = 1,
    ShadowOnly = 2,
    DoubleSided = 3
}

/// <summary>
/// Motion vector generation modes for motion blur and temporal effects.
/// </summary>
public enum MotionVectorMode
{
    Camera = 0,
    Object = 1,
    NoMotion = 2
}

