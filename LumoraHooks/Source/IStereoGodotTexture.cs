// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Godot.Hooks;

// A texture with a second picture for the right eye. GodotTexture2D on the base interface stays the
// left eye (and the only eye on a flat screen), so everything that already samples a texture keeps
// working; a shader that knows about VIEW_INDEX asks for this one too. -xlinka
public interface IStereoGodotTexture : IGodotTexture
{
    Texture2D? RightEyeTexture2D { get; }
}
