// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Scripting;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Hands an event that happened off the world thread, a websocket's message say, to the script running on
// a host: its Shared script on the authority, or this machine's Local one, whichever is running when the
// event lands on the world thread. A live graph swap in between hands it to the new graph; a script that
// stopped hears nothing. Queued like every event, never run where it arrived. -xlinka
internal static class ScriptHostEvents
{
    public static void Post<T>(ScriptHost? host, bool local, T payload) where T : struct
    {
        var world = host?.World;
        if (host == null || world == null || world.IsDisposed)
            return;
        world.RunSynchronously(() =>
        {
            if (host.IsDestroyed)
                return;
            var runtime = local ? host.LocalRuntime : host.SharedRuntime;
            if (runtime is not { CanRun: true })
                return;
            foreach (var entry in runtime.Runner.Instance.Events)
            {
                if (entry.PayloadType != typeof(T))
                    continue;
                var node = entry.NodeId;
                runtime.Enqueue(runner => runner.Raise(node, payload));
            }
        });
    }
}
