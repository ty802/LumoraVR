// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Helio.UI;

public readonly struct AssignedMaterial
{
    public readonly List<int> Indexes;
    public readonly MaterialMap Map;

    public AssignedMaterial(List<int> indexes, in MaterialMap map)
    {
        Indexes = indexes;
        Map = map;
    }
}
