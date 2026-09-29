// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Media;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Watches a player until it stops running: ended when it reached its end, stopped when it was held, stopped
// or taken away first, or was not there at all. A looping player only stops the second way. -xlinka
internal static class ScriptMediaWaits
{
    public static void UntilStopped(ScriptContext ctx, AwaitTicket<MediaStopped> ticket, IPlayable? playable)
    {
        float last = 0f;
        ScriptWaits.Watch(ctx, ticket, () =>
        {
            if (playable == null || playable is Component { IsDestroyed: true })
                return ("stopped", new MediaStopped(last));
            float position = playable.Position;
            if (float.IsFinite(position))
                last = position;
            if (playable.IsPlaying)
                return null;
            return (playable.IsFinished ? "ended" : "stopped", new MediaStopped(last));
        });
    }
}
