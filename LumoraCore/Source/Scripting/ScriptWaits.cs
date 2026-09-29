// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using Lumora.Script;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Scripting;

// What an await node leaves the world to watch: looked at once an update on the world thread, from the
// update after it starts, until the look says what to answer. A script that stops cancels the watch, and it
// ends without an answer; so does a look that throws. -xlinka
internal static class ScriptWaits
{
    public static void Watch<T>(ScriptContext ctx, AwaitTicket<T> ticket, Func<(string Exit, T Payload)?> look) where T : struct
        => ctx.World.StartCoroutine(Watching(ticket, look));

    private static IEnumerator Watching<T>(AwaitTicket<T> ticket, Func<(string Exit, T Payload)?> look) where T : struct
    {
        while (true)
        {
            if (ticket.Cancelled.IsCancellationRequested)
            {
                ticket.Abandon();
                yield break;
            }
            (string Exit, T Payload)? answer;
            try
            {
                answer = look();
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"Loom: a wait on {ticket.NodeId} stopped: {ex.Message}");
                ticket.Abandon();
                yield break;
            }
            if (answer is { } done)
            {
                ticket.Answer(done.Exit, done.Payload);
                yield break;
            }
            yield return null;
        }
    }
}
