// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Who let go and where the press last was, the same three ways as when it began.
public readonly record struct PressEnded(User? Presser, float3 Point, float2 LocalPoint, float2 NormalizedPoint);
