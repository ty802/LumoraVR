// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>A component that generates points in some spatial distribution (spawn areas, scatterers).</summary>
public interface IPointGenerator : IWorldElement
{
    /// <summary>Generate one point, expressed in the given space (world root when null).</summary>
    float3 GeneratePoint(Slot? space = null);
}
