// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// How far the tween got, 0 at its start and 1 at its end.
public readonly record struct TweenEnded(float Progress);
