// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A ValueNode whose two-input overloads may take more inputs of the same type, folded left to right:
// Add over a, b, c is (a + b) + c. A graph asks for it by listing every input type in "sig". Only an
// overload whose output is its input type folds.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class VariadicAttribute : Attribute
{
    public const int Limit = 16;

    public int MaxInputs { get; }

    public VariadicAttribute(int maxInputs = Limit)
    {
        MaxInputs = Math.Clamp(maxInputs, 2, Limit);
    }
}
