// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

// What a running world still has to do before it has settled; see World.CountSettle.
public readonly record struct WorldSettleCounts(int Objects, int Uploads, int Shaders)
{
    public int Total => Objects + Uploads + Shaders;
}
