// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

/// <summary>
/// Button whose hit area follows an ArcSegment graphic on the same slot.
/// </summary>
[ComponentCategory("UI/Helio/Interaction")]
public sealed class ArcButton : Button
{
    public override bool IsPointInside(in float2 point)
    {
        var arc = Slot?.GetComponent<ArcSegment>();
        return arc != null ? arc.IsPointInside(point) : base.IsPointInside(point);
    }
}
