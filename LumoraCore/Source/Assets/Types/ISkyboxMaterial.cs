// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// A material that can be the world's sky. Put on a Skybox it is drawn as the background and lights the
// world's ambient and reflections; on a mesh it is the same sky seen through the surface, which is what a
// sky orb shows. A material tool pointed at nothing puts one of these on the sky.
public interface ISkyboxMaterial : IAssetProvider<MaterialAsset>
{
}
