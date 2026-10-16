// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components;

// Stands for materials that live on a renderer somewhere else, so the material tool paints and samples
// through a slot that has none of its own: a collider kept apart from the mesh it stands for, or a rig root
// over a skinned body. Surfaces names the exact entries of a renderer's material list this slot shows;
// Renderer is a skinned renderer whose every surface it stands for. The tool looks for one on the hit slot
// and its parents before it looks for a renderer. -xlinka
[ComponentCategory("Rendering")]
public class MeshRendererMaterialRelay : Component
{
    public readonly SyncRef<SkinnedMeshRenderer> Renderer = null!;
    public readonly SyncRefList<AssetRef<MaterialAsset>> Surfaces = null!;

    public bool StandsForAnything
    {
        get
        {
            if (Renderer.Target is { IsDestroyed: false })
                return true;
            for (int i = 0; i < Surfaces.Count; i++)
            {
                if (Surfaces[i] is { IsDestroyed: false })
                    return true;
            }
            return false;
        }
    }

    // The material showing on the first surface this stands for.
    public IAssetProvider<MaterialAsset>? FirstMaterial
    {
        get
        {
            for (int i = 0; i < Surfaces.Count; i++)
            {
                if (Surfaces[i] is { IsDestroyed: false } surface && surface.Target != null)
                    return surface.Target;
            }
            return Renderer.Target is { IsDestroyed: false } skinned ? skinned.MaterialFor(0) : null;
        }
    }
}
