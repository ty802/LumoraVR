// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Whether the material tool may paint this slot and everything under it. The nearest enabled one above a
// surface decides, so one with AllowPaint on inside a refused branch opens that part again. A tooling rule,
// like GrabBlock: it stops a person with the tool, never code writing a material. -xlinka
[ComponentCategory("Interaction/Tools")]
[SingleInstancePerSlot]
public sealed class MaterialPaintPolicy : Component
{
    public readonly Sync<bool> AllowPaint;

    public MaterialPaintPolicy()
    {
        AllowPaint = new Sync<bool>(this, false);
    }

    // False when the nearest enabled policy at or above the slot refuses paint.
    public static bool Allows(Slot? slot)
    {
        var policy = slot?.GetComponentInParents<MaterialPaintPolicy>(p => p.Enabled.Value);
        return policy == null || policy.AllowPaint.Value;
    }
}
