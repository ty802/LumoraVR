// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// "n1.next" to "n4.in" for flow, "n2.value" to "n3.a" for data.
public readonly record struct GraphWire(string FromNode, string FromPort, string ToNode, string ToPort)
{
    public override string ToString() => $"{FromNode}.{FromPort} -> {ToNode}.{ToPort}";
}
