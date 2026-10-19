// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// The solid slab behind one card, in the backing mesh's own space: its middle, which way it faces, its
// size and the rounding of its corners, in metres.
public readonly record struct ScriptCardSlab(float3 Center, floatQ Rotation, float2 Size, float CornerRadius);
