// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Who pressed, and where. The presser is read by the host from the relay's byte, never from a field
// the pressing client wrote.
public readonly record struct ButtonPressed(User? Presser, float3 Point);
