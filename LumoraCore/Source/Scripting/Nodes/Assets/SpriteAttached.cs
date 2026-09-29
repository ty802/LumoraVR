// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Scripting;

// What Attach Sprite came back with: the texture and the sprite drawn from it, or why there are none.
public readonly record struct SpriteAttached(IAssetProvider<TextureAsset>? Texture, Component? Sprite, string Error);
