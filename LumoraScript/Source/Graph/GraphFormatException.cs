// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

public sealed class GraphFormatException : Exception
{
    public GraphFormatException(string message) : base(message)
    {
    }
}
