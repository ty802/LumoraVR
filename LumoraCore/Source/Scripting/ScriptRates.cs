// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// How often one script does something that costs, counted over a window of its own clock, for the nodes
// that warn past a rate. Nothing is refused: the script is warned the first time a window passes the mark.
internal static class ScriptRates
{
    private sealed class Windows
    {
        public readonly Dictionary<ScriptWarning, (double Start, int Count)> ByKind = new();
    }

    private static readonly ConditionalWeakTable<ScriptContext, Windows> _windows = new();

    public static void Count(ScriptContext ctx, ScriptWarning kind, int mark, double seconds, string what)
    {
        var windows = _windows.GetOrCreateValue(ctx);
        double now = ctx.Now;
        if (!windows.ByKind.TryGetValue(kind, out var window) || now - window.Start >= seconds)
            window = (now, 0);
        window.Count++;
        windows.ByKind[kind] = window;
        if (window.Count == mark + 1)
            ctx.Warn(kind, seconds <= 1.0 ? $"more than {mark} {what} in a second" : $"more than {mark} {what} in {seconds:0} seconds");
    }
}
