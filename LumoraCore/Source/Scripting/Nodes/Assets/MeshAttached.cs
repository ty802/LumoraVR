// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Scripting;

// What Attach Mesh came back with: the mesh on the slot, or why there is none.
public readonly record struct MeshAttached(IAssetProvider<MeshDataAsset>? Mesh, string Error);
