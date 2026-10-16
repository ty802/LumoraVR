// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

// A drive that, while it holds, refuses every write but its own: a hand write, a grab, another component's
// set all land in its hook instead of the field, which keeps the driven value. Not holding, it is a plain
// drive and takes writes the way any drive does. -xlinka
public class HoldingFieldDrive<T> : FieldDrive<T>
{
    public HoldingFieldDrive()
    {
    }

    public HoldingFieldDrive(IWorldElement? owner) : base(owner)
    {
    }

    public Func<bool>? Holds { get; set; }

    public override bool IsModificationAllowed => Holds?.Invoke() != true;
}
