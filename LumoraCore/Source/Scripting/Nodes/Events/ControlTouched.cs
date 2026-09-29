// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Who worked the control and how: began is the first frame of a press, held every frame after while it
// is worked, ended the frame it comes back up; where the touch met the surface and where the fingertip or
// beam tip itself was, in world space; the hand; and whether it was a fingertip rather than a beam.
public readonly record struct ControlTouched(User? User, bool Began, bool Held, bool Ended, float3 Point, float3 Tip, Chirality Hand,
    bool Fingertip);
