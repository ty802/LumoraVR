// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Whose beam clicked the object, and where on it.
public readonly record struct BeamClicked(User? User, float3 Point);
