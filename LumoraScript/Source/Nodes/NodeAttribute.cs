// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Makes a class a node. The path is where the browser files it; the class name is what a graph calls
// it. Bump Version when the ports change, and give the class a static Migrate for older graphs.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NodeAttribute : Attribute
{
    public string Path { get; }
    public Capability Capability { get; }
    public int Version { get; init; } = 1;

    public NodeAttribute(string path, Capability capability = Capability.Core)
    {
        Path = path;
        Capability = capability;
    }
}
