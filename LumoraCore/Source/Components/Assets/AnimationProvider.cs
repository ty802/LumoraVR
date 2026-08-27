// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

// the asset gathers and decodes itself through AssetManager; this only resolves and requests the URL.
// pointing at a local:// URI is what makes a clip travel: content-addressed in the local DB, replicated
// with the component, and gathered by hash on join
[ComponentCategory("Assets")]
public sealed class AnimationProvider : StaticAssetProvider<AnimationAsset>
{
}
