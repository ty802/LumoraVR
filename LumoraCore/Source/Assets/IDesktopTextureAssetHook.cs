// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Desktop;

namespace Lumora.Core.Assets;

// The renderer side of a desktop texture: pulls one layer of a frame source on the main thread and keeps
// uploading it into the same texture object, so a material that bound it once sees every change. -xlinka
public interface IDesktopTextureAssetHook : ITextureAssetHook
{
    // A null source stops pulling; the last picture stays up.
    void Bind(IDesktopFrameSource? source, int layer);

    // Raised when the texture object to bind changed (above all the first picture), not on new pixels.
    event Action TextureChanged;

    // Moves on with every upload of new pixels. Whatever captures this texture offscreen polls it to know
    // when to capture again: pixels changing in place never touch a mesh.
    int Uploads { get; }
}
