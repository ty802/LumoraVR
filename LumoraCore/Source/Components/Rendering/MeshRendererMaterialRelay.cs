// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// attach to the rig root to control materials on skinned meshes
[ComponentCategory("Rendering")]
public class MeshRendererMaterialRelay : Component
{
    public readonly SyncRef<SkinnedMeshRenderer> Renderer = null!;
}
