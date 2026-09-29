// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// One foot of an avatar coming down: which one, where its sole met the ground and how it was turned, in
// world space, how fast the body was going in metres a second, and whether it was the landing after being
// off the ground rather than a step.
public readonly record struct AvatarFootstep(Chirality Side, float3 Position, floatQ Rotation, float Speed, bool Landing);
