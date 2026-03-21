// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

/// <summary>
/// Relays material changes to a mesh renderer.
/// Attach to the rig root to control materials on skinned meshes.
/// </summary>
[ComponentCategory("Rendering")]
public class MeshRendererMaterialRelay : Component
{
    /// <summary>
    /// The renderer whose materials this relay controls.
    /// </summary>
    public readonly SyncRef<SkinnedMeshRenderer> Renderer;
}
