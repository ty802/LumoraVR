// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Phos;

/// <summary>
/// Reference to a point in a point submesh.
/// Placeholder for future point cloud support.
/// </summary>
public struct PhosPoint
{
    private int index;
    private int version;
    // TODO: Add PhosPointSubmesh when implementing point clouds

    public int Index => index;
}