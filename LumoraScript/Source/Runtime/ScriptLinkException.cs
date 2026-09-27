// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A compiled script could not be set up: an import its context refuses, or bindings that do not fit.
public sealed class ScriptLinkException : Exception
{
    public ScriptLinkException(string message) : base(message)
    {
    }
}
