// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// returned by IInteractionTarget to describe what an interaction does, how the cursor
// should look, and whether to force-click on hit. - xlinka
public struct InteractionDescription
{
    public LaserCursor? Cursor;
    public string? Name;
    public bool ForceActivate;
    public color? OverrideHitColor;
}
