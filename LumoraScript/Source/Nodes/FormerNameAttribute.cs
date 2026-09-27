// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// An old class name graphs may still use for this node, kept when a node is renamed.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FormerNameAttribute : Attribute
{
    public string Name { get; }

    public FormerNameAttribute(string name)
    {
        Name = name;
    }
}
