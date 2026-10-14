// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Who is still pressing and where the press is now, the same three ways as when it began.
public readonly record struct PressHeld(User? Presser, float3 Point, float2 LocalPoint, float2 NormalizedPoint);
