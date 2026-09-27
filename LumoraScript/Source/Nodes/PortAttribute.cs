// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Renames a port. Without it a parameter's name is its port, and a return value is "result".
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
public sealed class PortAttribute : Attribute
{
    public string Name { get; }

    public PortAttribute(string name)
    {
        Name = name;
    }
}
