// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class InteractionBlock : UIComponent
{
    public readonly Sync<bool> Blocks;

    public InteractionBlock()
    {
        Blocks = new Sync<bool>(this, true);
    }

    public bool BlocksPoint(in float2 point)
    {
        return Enabled.Value
            && Blocks.Value
            && Slot != null
            && Slot.IsActive
            && (RectTransform?.LocalComputeRect.Contains(point) ?? false);
    }
}
