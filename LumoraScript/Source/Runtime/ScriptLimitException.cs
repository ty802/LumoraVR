// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A script hit one of its runtime caps. Thrown out of a host call, so the engine turns it into a trap
// and the current call ends there.
public sealed class ScriptLimitException : Exception
{
    public ScriptLimitException(string message) : base(message)
    {
    }
}
