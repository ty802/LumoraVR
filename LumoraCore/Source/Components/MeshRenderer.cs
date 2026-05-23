// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

﻿using Lumora.Core;
using Lumora.Core.Assets;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Renders a 3D mesh.
/// </summary>
[ComponentCategory("Rendering")]
public class MeshRenderer : ImplementableComponent
{
    /// <summary>
    /// The mesh to render (ProceduralMesh or MeshDataAsset component).
    /// Uses SyncRef to properly sync component references over network.
    /// </summary>
    public readonly SyncRef<Component> Mesh;

    public readonly SyncAssetList<MaterialAsset> Materials;
    public readonly SyncAssetList<MaterialPropertyBlockAsset> MaterialPropertyBlocks;

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

    // Mesh DATA changing (ClearSurfaces + re-add) is invisible to the SyncRef WasChanged path,
    // so callers that rebuild the underlying ArrayMesh contents in place must flag us dirty
    // explicitly or surface override materials end up stale. - xlinka
    public void FlagSurfacesDirty()
    {
        MaterialsChanged = true;
        MaterialPropertyBlocksChanged = true;
        RunApplyChanges();
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
        ShadowCastMode = new Sync<ShadowCastMode>(this, Components.ShadowCastMode.On);
        SortingOrder = new Sync<int>(this, 0);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Materials.OnChanged += OnMaterialsChanged;
        MaterialPropertyBlocks.OnChanged += OnMaterialPropertyBlocksChanged;
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

    public override void OnDestroy()
    {
        Materials.OnChanged -= OnMaterialsChanged;
        MaterialPropertyBlocks.OnChanged -= OnMaterialPropertyBlocksChanged;
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
