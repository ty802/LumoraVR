// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Godot.Hooks;

public interface IGodotTexture
{
    Texture2D? GodotTexture2D { get; }
    bool IsValid { get; }
}
