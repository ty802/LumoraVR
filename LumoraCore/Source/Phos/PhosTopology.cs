// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Phos;

/// <summary>
/// Mesh topology types.
/// Defines how vertex indices are interpreted to form primitives.
/// </summary>
public enum PhosTopology
{
    /// <summary>Triangles - 3 vertices per primitive</summary>
    Triangles,

    /// <summary>Quads - 4 vertices per primitive</summary>
    Quads,

    /// <summary>Lines - 2 vertices per primitive</summary>
    Lines,

    /// <summary>Points - 1 vertex per primitive</summary>
    Points
}