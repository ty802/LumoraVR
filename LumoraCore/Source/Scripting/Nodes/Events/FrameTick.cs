// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Delta is the world's frame time in seconds, from World.Time.
public readonly record struct FrameTick(float Delta);
