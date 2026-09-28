// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// The date in the time zone of the machine running the script; an unspecified date counts as UTC. A
// machine's zone is its own, so only a Local script converts: a Shared script runs on the host, and is
// refused rather than handed the host's zone.
[Node("Time/Dates/To Local Time")]
public sealed class DateToLocal : ReadNode
{
    [return: Port("local")]
    public static DateTime Run(ScriptContext ctx, DateTime date)
    {
        if (!ctx.IsLocal)
            throw new UnauthorizedAccessException("only a Local script reads its machine's time zone; a Shared script runs on the host");
        return date.ToLocalTime();
    }
}
