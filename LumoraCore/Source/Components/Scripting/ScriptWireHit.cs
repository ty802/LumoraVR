// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// Where a ray meets a wire: which wire, how far along its curve from the output end, the point on the curve in
// the graph's own space, and how far along the ray that is, with the point the ray passes nearest it.
public readonly record struct ScriptWireHit(ScriptWireKey Key, float Along, float3 Point, float Distance, float3 RayPoint);
