// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Whose foot came down and which one, where its sole met the ground and how it was turned, how fast they
// were going in metres a second, whether it was a landing after being off the ground, and the slot under
// the foot when there is one the script can see.
public readonly record struct Footstep(User? User, Chirality Side, float3 Position, floatQ Rotation, float Speed, bool Landing, Slot? Ground);
