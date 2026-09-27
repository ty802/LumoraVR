// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// The shape a graph document is held to: how deep its JSON nests, how many numbers one value holds (the
// widest type holds 16) and how long a signature is, which also bounds how deep a type can nest. Nothing
// here caps how big a graph is: its nodes, wires, bindings and texts are as many and as long as it has.
public sealed class GraphLimits
{
    public static readonly GraphLimits Default = new();

    public int MaxDepth { get; init; } = 16;
    public int MaxNumbersPerValue { get; init; } = 16;
    public int MaxSignatureLength { get; init; } = 256;
}
