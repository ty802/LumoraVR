// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

/// <summary>
/// Component that provides shader source assets from URLs. The asset gathers and decodes itself
/// through the <see cref="AssetManager"/>; this component just resolves the URL and requests it.
/// </summary>
[ComponentCategory("Assets")]
public sealed class ShaderSourceProvider : StaticAssetProvider<ShaderSourceAsset>
{
}
