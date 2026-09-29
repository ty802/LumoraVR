// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;

namespace Lumora.Core.Scripting;

// What Attach Audio Clip came back with: the clip on the slot, or why there is none.
public readonly record struct ClipAttached(IAssetProvider<AudioClip>? Clip, string Error);
