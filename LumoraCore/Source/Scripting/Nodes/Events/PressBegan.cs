// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Who pressed and where: in the world, in the pressed thing's own layout space, and across its rect from the
// lower left corner (0, 0) to the upper right (1, 1).
public readonly record struct PressBegan(User? Presser, float3 Point, float2 LocalPoint, float2 NormalizedPoint);
