// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives a plain color field from a colorHDR one: a material's or a variable's colour onto an interface tint. The
// CopyValue for the one pair of types a drive cannot cross. The colour carries over as it is; nothing is clamped.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class ColorFromHdr : Component
{
    public readonly SyncRef<IField<colorHDR>> Source;

    public readonly FieldDrive<color> Target;

    public ColorFromHdr()
    {
        Source = new SyncRef<IField<colorHDR>>(this);
        Target = new FieldDrive<color>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (Source.Target is not { } source || !Target.IsLinkValid)
            return;
        var value = source.Value;
        Target.SetValue(new color(value.r, value.g, value.b, value.a));
    }
}
