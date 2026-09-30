// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Shading;

// On an output node's input: what feeds it is worked out per vertex, not per pixel.
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class VertexStageAttribute : Attribute
{
}
