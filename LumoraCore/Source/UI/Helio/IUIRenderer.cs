// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Phos;

namespace Helio.UI;

/// <summary>
/// A type that can generate mesh geometry into a target PhosMesh.
/// Implemented by UI elements that act as standalone renderables (rather than participating in canvas batching).
/// </summary>
public interface IUIRenderer
{
    /// <summary>
    /// Append this renderer's geometry to the target mesh.
    /// </summary>
    void Generate(PhosMesh mesh);
}
