// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// The exits an await node's answer can run, in the order they sit on the card after next.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnswersAttribute : Attribute
{
    public string[] Exits { get; }

    public AnswersAttribute(params string[] exits)
    {
        Exits = exits;
    }
}
