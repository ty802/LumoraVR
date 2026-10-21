// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// What the curve editor needs to know about one track before it reads a key: where it sits in the list,
// what its channels are called, and which edits it can take.
public sealed class CurveTrackInfo
{
    // The node a clip track animates, or the component that owns a curve field.
    public string Group = "";
    public string Name = "";
    public string[] Channels = System.Array.Empty<string>();

    public bool Editable = true;
    // Shown as Euler degrees, held as a quaternion.
    public bool Rotation;
    // Whole numbers only: an int, a long, a bool as 0 or 1.
    public bool Whole;
    // A value with no midpoint: every key holds until the next.
    public bool HoldOnly;
    public bool AllowHold = true;
    public bool AllowSmooth = true;
    // Shown when Editable is false.
    public string ReadOnlyReason = "";

    public string Label => string.IsNullOrEmpty(Group) ? Name : $"{Group} · {Name}";
}
