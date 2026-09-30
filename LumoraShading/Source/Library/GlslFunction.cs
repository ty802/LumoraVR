// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Shading;

// One helper function a shader can include, with the helpers it calls. Written once and included only
// when a node that calls it is in the graph.
public sealed class GlslFunction
{
    public string Name { get; }
    public string Code { get; }
    public IReadOnlyList<string> Needs { get; }

    // for loops in the body, counted against the sandbox's loop allowance.
    public int Loops { get; }

    public GlslFunction(string name, string code, params string[] needs)
    {
        Name = name;
        Code = code.Replace("\r\n", "\n").Trim('\n');
        Needs = needs;
        int loops = 0;
        for (int at = Code.IndexOf("for (", StringComparison.Ordinal); at >= 0; at = Code.IndexOf("for (", at + 5, StringComparison.Ordinal))
            loops++;
        Loops = loops;
    }
}
